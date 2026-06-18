using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;
using TechNormBlazor.Services.ConformanceChecking;

namespace TechNormBlazor.Services;

public interface IConformanceCheckingService
{
    Task<ConformanceCheckResult?> CheckProductRouteAsync(
        int productId,
        int? routeId = null,
        string trigger = "manual",
        CancellationToken ct = default);

    Task TriggerRecalculationAsync(int productId, CancellationToken ct = default);
}

public class ConformanceCheckingService(
    IDbContextFactory<TechNormDbContext> factory,
    ILogger<ConformanceCheckingService>  logger) : IConformanceCheckingService
{
    private const decimal WeightTime     = 0.35m;
    private const decimal WeightMaterial = 0.25m;
    private const decimal WeightRoute    = 0.30m;
    private const decimal WeightResource = 0.10m;

    private const decimal SeverityTimeHigh   = 20m;
    private const decimal SeverityTimeMedium = 10m;

    private const decimal SeverityMatMedium = 10m;

    private const decimal ThresholdConformant  = 85m;
    private const decimal ThresholdNeedsReview = 70m;

    // SQL result DTOs for aggregation queries
    private sealed class SqlCountRow
    {
        public int case_count  { get; set; }
        public int event_count { get; set; }
    }

    private sealed class SqlTransitionRow
    {
        public string from_act { get; set; } = "";
        public string to_act   { get; set; } = "";
        public int    cnt      { get; set; }
    }

    private sealed class SqlDurationRow
    {
        public string activity     { get; set; } = "";
        public double avg_min      { get; set; }
        public int    sample_count { get; set; }
    }

    private sealed class SqlResourceRow
    {
        public string activity      { get; set; } = "";
        public int    resource_id   { get; set; }
        public string resource_name { get; set; } = "";
        public int    usage_count   { get; set; }
    }

    public async Task TriggerRecalculationAsync(int productId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var route = await db.TechRoutes
                .Where(r => r.ProductId == productId && r.Status == "published")
                .FirstOrDefaultAsync(ct);

            if (route is null) return;

            await CheckProductRouteAsync(productId, route.Id, trigger: "event_added", ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "PCI recalculation failed for product {ProductId}", productId);
        }
    }

    public async Task<ConformanceCheckResult?> CheckProductRouteAsync(
        int productId,
        int? routeId = null,
        string trigger = "manual",
        CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        TechRoute? route = routeId.HasValue
            ? await db.TechRoutes
                .Include(r => r.Steps.OrderBy(s => s.SequenceNum))
                    .ThenInclude(s => s.Operation)
                .Include(r => r.Steps)
                    .ThenInclude(s => s.TimeNorms)
                        .ThenInclude(t => t.Resource)
                .Include(r => r.Steps)
                    .ThenInclude(s => s.MaterialNorms)
                        .ThenInclude(mn => mn.Material)
                .FirstOrDefaultAsync(r => r.Id == routeId.Value, ct)
            : await db.TechRoutes
                .Where(r => r.ProductId == productId && r.Status == "published")
                .Include(r => r.Steps.OrderBy(s => s.SequenceNum))
                    .ThenInclude(s => s.Operation)
                .Include(r => r.Steps)
                    .ThenInclude(s => s.TimeNorms)
                        .ThenInclude(t => t.Resource)
                .Include(r => r.Steps)
                    .ThenInclude(s => s.MaterialNorms)
                        .ThenInclude(mn => mn.Material)
                .FirstOrDefaultAsync(ct);

        if (route is null)
        {
            logger.LogInformation(
                "ConformanceCheck: no route found for product {ProductId}", productId);
            return null;
        }

        // Count cases/events without loading any rows into memory
        var countRow = (await db.Database.SqlQuery<SqlCountRow>(
            $"""
            SELECT COUNT(DISTINCT case_id)::int AS case_count,
                   COUNT(*)::int                AS event_count
            FROM event_logs
            WHERE product_id = {productId}
            """).ToListAsync(ct)).FirstOrDefault();

        if (countRow is null || countRow.event_count == 0)
        {
            logger.LogInformation(
                "ConformanceCheck: no events for product {ProductId}", productId);
            return null;
        }

        // Actual transitions via LEAD — covers ALL events in one pass
        var transRows = await db.Database.SqlQuery<SqlTransitionRow>(
            $"""
            WITH ordered AS (
                SELECT case_id, activity,
                       LEAD(activity) OVER (PARTITION BY case_id ORDER BY timestamp) AS next_act
                FROM event_logs
                WHERE product_id = {productId}
                  AND activity IS NOT NULL AND activity <> ''
            )
            SELECT activity AS from_act, next_act AS to_act, COUNT(*)::int AS cnt
            FROM ordered
            WHERE next_act IS NOT NULL AND activity <> next_act
            GROUP BY activity, next_act
            """).ToListAsync(ct);

        // Average duration per activity across all events
        var durRows = await db.Database.SqlQuery<SqlDurationRow>(
            $"""
            SELECT activity,
                   AVG(EXTRACT(EPOCH FROM duration) / 60.0)::float8 AS avg_min,
                   COUNT(*)::int                                      AS sample_count
            FROM event_logs
            WHERE product_id = {productId}
              AND duration IS NOT NULL
              AND activity IS NOT NULL AND activity <> ''
            GROUP BY activity
            """).ToListAsync(ct);

        // Resource usage per (activity, resource) pair
        var resRows = await db.Database.SqlQuery<SqlResourceRow>(
            $"""
            SELECT e.activity,
                   e.resource_id::int                                  AS resource_id,
                   COALESCE(r.name, 'Ресурс ' || e.resource_id::text) AS resource_name,
                   COUNT(*)::int                                       AS usage_count
            FROM event_logs e
            LEFT JOIN resources r ON e.resource_id = r.id
            WHERE e.product_id = {productId}
              AND e.resource_id IS NOT NULL
              AND e.activity IS NOT NULL AND e.activity <> ''
            GROUP BY e.activity, e.resource_id, r.name
            """).ToListAsync(ct);

        var orderedSteps = route.Steps.OrderBy(s => s.SequenceNum).ToList();

        var actualTransitions   = transRows.ToDictionary(r => (r.from_act, r.to_act), r => r.cnt);
        var expectedTransitions = BuildExpectedTransitions(orderedSteps);

        var transResult = CheckTransitions(expectedTransitions, actualTransitions);
        var timeResult  = CheckTimeNormsFromAgg(orderedSteps, durRows);
        var matResult   = new MaterialCheckResult(0m, false, []);
        var resResult   = CheckResourcesFromAgg(orderedSteps, resRows);

        decimal pci = CalculatePci(
            transResult.RouteDeviation,
            timeResult.Avg,
            matResult.Avg,
            resResult.Deviation,
            hasTimeData:     timeResult.HasData,
            hasMaterialData: matResult.HasData,
            hasResourceData: resResult.HasData);

        string status  = GetStatus(pci);
        string summary = GetSummary(pci, transResult, timeResult, matResult, resResult);

        var result = new ConformanceCheckResult
        {
            ProductId                 = productId,
            RouteId                   = route.Id,
            TotalTraceCount           = countRow.case_count,
            CheckedTraceCount         = countRow.case_count,
            TotalEventCount           = countRow.event_count,
            ExpectedTransitionCount   = transResult.ExpectedCount,
            ActualTransitionCount     = transResult.ActualCount,
            MatchedTransitionCount    = transResult.MatchedCount,
            UnexpectedTransitionCount = transResult.UnexpectedCount,
            MissingTransitionCount    = transResult.MissingCount,
            TimeDeviationAvg          = timeResult.Avg,
            MaterialDeviationAvg      = matResult.Avg,
            RouteDeviation            = transResult.RouteDeviation,
            ResourceDeviation         = resResult.Deviation,
            ProcessConformanceIndex   = pci,
            Status                    = status,
            Summary                   = summary,
            CalculatedAt              = DateTime.UtcNow,
            Trigger                   = trigger,
            OperationIssues           = timeResult.Issues,
            TransitionIssues          = transResult.Issues,
            MaterialIssues            = matResult.Issues,
            ResourceIssues            = resResult.Issues,
        };

        await SaveConformanceMetricsAsync(db, result, ct);
        await UpdateRoutePciStatusAsync(db, route.Id, result, ct);

        return result;
    }

    private static HashSet<(string From, string To)> BuildExpectedTransitions(
        List<RouteStep> steps)
    {
        var result = new HashSet<(string, string)>();
        for (int i = 0; i < steps.Count - 1; i++)
        {
            var a = StepName(steps[i]);
            var b = StepName(steps[i + 1]);
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b))
                result.Add((a, b));
        }
        return result;
    }

    private record TransitionCheckResult(
        int ExpectedCount, int ActualCount, int MatchedCount,
        int UnexpectedCount, int MissingCount, decimal RouteDeviation,
        List<TransitionConformanceIssue> Issues);

    private static TransitionCheckResult CheckTransitions(
        HashSet<(string From, string To)> expected,
        Dictionary<(string, string), int> actual)
    {
        var issues = new List<TransitionConformanceIssue>();

        var normExpected = expected
            .Select(t => (Norm(t.From), Norm(t.To)))
            .ToHashSet();

        int matched    = 0;
        int unexpected = 0;
        var missingSet = new HashSet<(string, string)>(normExpected);

        foreach (var (key, count) in actual)
        {
            var normKey = (Norm(key.Item1), Norm(key.Item2));
            if (normExpected.Contains(normKey))
            {
                matched++;
                missingSet.Remove(normKey);
            }
            else
            {
                unexpected++;
                issues.Add(new TransitionConformanceIssue
                {
                    FromOperation = key.Item1,
                    ToOperation   = key.Item2,
                    ActualCount   = count,
                    ExistsInRoute = false,
                    Severity      = count <= 2 ? "Low" : "High",
                    Message       = $"Переход «{key.Item1} → {key.Item2}» ({count}×) отсутствует в утверждённом маршруте.",
                });
            }
        }

        int missing = missingSet.Count;
        foreach (var (f, t) in missingSet)
        {
            issues.Add(new TransitionConformanceIssue
            {
                FromOperation = f,
                ToOperation   = t,
                ActualCount   = 0,
                ExistsInRoute = true,
                Severity      = "Medium",
                Message       = $"Нормативный переход «{f} → {t}» не встречается в фактических событиях.",
            });
        }

        int total   = Math.Max(expected.Count + actual.Count, 1);
        decimal dev = Math.Round((decimal)(unexpected + missing) / total * 100m, 2);

        return new TransitionCheckResult(
            expected.Count, actual.Count, matched,
            unexpected, missing, dev, issues);
    }

    private record TimeCheckResult(decimal Avg, bool HasData, List<OperationConformanceIssue> Issues);

    private static TimeCheckResult CheckTimeNormsFromAgg(
        List<RouteStep> steps,
        List<SqlDurationRow> durRows)
    {
        var issues       = new List<OperationConformanceIssue>();
        decimal wSum     = 0m;
        int totalSamples = 0;
        bool hasData     = false;

        var byActivity = durRows.ToDictionary(
            r => r.activity, r => r, StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            string opName = StepName(step);
            decimal? norm = BestNorm(step);
            if (norm is null or 0) continue;

            if (!byActivity.TryGetValue(opName, out var row) || row.sample_count == 0) continue;

            hasData = true;
            decimal avg = (decimal)row.avg_min;
            decimal dev = norm.Value > 0
                ? Math.Round(Math.Abs(avg - norm.Value) / norm.Value * 100m, 2)
                : 0m;

            wSum         += dev * row.sample_count;
            totalSamples += row.sample_count;

            if (dev >= SeverityTimeMedium)
                issues.Add(new OperationConformanceIssue
                {
                    OperationName    = opName,
                    NormTime         = norm,
                    ActualTime       = Math.Round(avg, 2),
                    DeviationPercent = dev,
                    Severity         = dev >= SeverityTimeHigh ? "High" : "Medium",
                    Message          = $"Операция «{opName}»: факт {avg:F1} мин, норма {norm:F1} мин, отклонение {dev:F1}%.",
                });
        }

        decimal avgDev = totalSamples > 0 ? Math.Round(wSum / totalSamples, 2) : 0m;
        return new TimeCheckResult(avgDev, hasData, issues);
    }

    private record MaterialCheckResult(decimal Avg, bool HasData, List<MaterialConformanceIssue> Issues);

    private record ResourceCheckResult(decimal Deviation, bool HasData, List<ResourceConformanceIssue> Issues);

    private static ResourceCheckResult CheckResourcesFromAgg(
        List<RouteStep> steps,
        List<SqlResourceRow> resRows)
    {
        var issues = new List<ResourceConformanceIssue>();

        var allowedResources = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            string opName = StepName(step);
            var ids = step.TimeNorms
                .Where(t => t.ResourceId.HasValue)
                .Select(t => t.ResourceId!.Value)
                .ToHashSet();
            if (ids.Count > 0)
                allowedResources[opName] = ids;
        }

        if (allowedResources.Count == 0)
            return new ResourceCheckResult(0m, false, issues);

        int totalUsages   = 0;
        int invalidUsages = 0;

        foreach (var row in resRows)
        {
            if (!allowedResources.TryGetValue(row.activity, out var allowed)) continue;

            totalUsages += row.usage_count;

            if (!allowed.Contains(row.resource_id))
            {
                invalidUsages += row.usage_count;
                decimal share = Math.Round(row.usage_count * 100m / Math.Max(row.usage_count, 1), 2);
                issues.Add(new ResourceConformanceIssue
                {
                    OperationName     = row.activity,
                    ResourceName      = row.resource_name,
                    IsAllowedResource = false,
                    UsageCount        = row.usage_count,
                    UsageShare        = share,
                    Severity          = "High",
                    Message           = $"Операция «{row.activity}»: ресурс «{row.resource_name}» не допустим согласно маршруту ({row.usage_count}×).",
                });
            }
        }

        bool hasData = totalUsages > 0;
        decimal dev  = hasData
            ? Math.Round((decimal)invalidUsages / totalUsages * 100m, 2)
            : 0m;
        return new ResourceCheckResult(dev, hasData, issues);
    }

    private static decimal CalculatePci(
        decimal routeDev,
        decimal timeDev,
        decimal matDev,
        decimal resDev,
        bool hasTimeData,
        bool hasMaterialData,
        bool hasResourceData)
    {
        decimal wTime  = hasTimeData     ? WeightTime     : 0m;
        decimal wMat   = hasMaterialData ? WeightMaterial : 0m;
        decimal wRes   = hasResourceData ? WeightResource : 0m;
        decimal wRoute = WeightRoute;

        decimal totalWeight = wTime + wMat + wRoute + wRes;
        if (totalWeight <= 0m) totalWeight = 1m;

        wTime  /= totalWeight;
        wMat   /= totalWeight;
        wRoute /= totalWeight;
        wRes   /= totalWeight;

        decimal penalty = wRoute * routeDev
                        + wTime  * timeDev
                        + wMat   * matDev
                        + wRes   * resDev;

        return Math.Round(Math.Max(0m, Math.Min(100m, 100m - penalty)), 2);
    }

    private static async Task SaveConformanceMetricsAsync(
        TechNormDbContext db, ConformanceCheckResult result, CancellationToken ct)
    {
        var metricsObj = new
        {
            type                      = "ConformanceChecking",
            productId                 = result.ProductId,
            routeId                   = result.RouteId,
            pci                       = result.ProcessConformanceIndex,
            status                    = result.Status,
            summary                   = result.Summary,
            trigger                   = result.Trigger,
            timeDeviationAvg          = result.TimeDeviationAvg,
            materialDeviationAvg      = result.MaterialDeviationAvg,
            routeDeviation            = result.RouteDeviation,
            resourceDeviation         = result.ResourceDeviation,
            totalTraceCount           = result.TotalTraceCount,
            checkedTraceCount         = result.CheckedTraceCount,
            totalEventCount           = result.TotalEventCount,
            expectedTransitionCount   = result.ExpectedTransitionCount,
            actualTransitionCount     = result.ActualTransitionCount,
            matchedTransitionCount    = result.MatchedTransitionCount,
            unexpectedTransitionCount = result.UnexpectedTransitionCount,
            missingTransitionCount    = result.MissingTransitionCount,
            calculatedAt              = result.CalculatedAt,
            issues = new
            {
                operations  = result.OperationIssues,
                transitions = result.TransitionIssues,
                materials   = result.MaterialIssues,
                resources   = result.ResourceIssues,
            },
        };

        var history = new CalculationHistory
        {
            ProductId    = result.ProductId,
            RouteId      = result.RouteId,
            CalculatedAt = result.CalculatedAt,
            Metrics      = JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(metricsObj)),
        };
        db.CalculationHistories.Add(history);
        await db.SaveChangesAsync(ct);
    }

    private static async Task UpdateRoutePciStatusAsync(
        TechNormDbContext db, int routeId, ConformanceCheckResult result, CancellationToken ct)
    {
        var route = await db.TechRoutes.FindAsync([routeId], ct);
        if (route is null) return;

        route.LastPci             = result.ProcessConformanceIndex;
        route.LastPciStatus       = result.Status;
        route.LastPciCalculatedAt = result.CalculatedAt;
        route.LastPciSummary      = result.Summary;
        await db.SaveChangesAsync(ct);
    }

    private static string StepName(RouteStep step) =>
        step.Operation?.Name ?? step.Description ?? $"Шаг {step.SequenceNum}";

    private static string Norm(string s) => s?.Trim().ToLowerInvariant() ?? "";

    private static decimal? BestNorm(RouteStep step)
    {
        var norms = step.TimeNorms.Where(t => t.NormValue > 0).Select(t => t.NormValue).ToList();
        if (norms.Count == 0) return null;
        return norms.Average();
    }

    private static string GetStatus(decimal pci) => pci switch
    {
        >= ThresholdConformant  => "Conformant",
        >= ThresholdNeedsReview => "NeedsReview",
        _                       => "NonConformant",
    };

    private static string GetSummary(
        decimal pci,
        TransitionCheckResult trans,
        TimeCheckResult time,
        MaterialCheckResult mat,
        ResourceCheckResult res)
    {
        if (pci >= ThresholdConformant)
            return "Фактическое выполнение соответствует утверждённой маршрутной карте.";

        var parts = new List<string>();
        if (trans.UnexpectedCount > 0 || trans.MissingCount > 0)
            parts.Add($"{trans.UnexpectedCount} неожиданных и {trans.MissingCount} отсутствующих переходов");
        if (time.Avg > SeverityTimeMedium)
            parts.Add($"среднее отклонение времени {time.Avg:F1}%");
        if (mat.HasData && mat.Avg > SeverityMatMedium)
            parts.Add($"отклонение материалов {mat.Avg:F1}%");
        if (res.HasData && res.Deviation > 0)
            parts.Add($"недопустимые ресурсы {res.Deviation:F1}%");

        string reasons = parts.Count > 0
            ? string.Join("; ", parts) + "."
            : "требуется проверка.";

        return pci >= ThresholdNeedsReview
            ? $"Обнаружены отклонения, требуется проверка технологом: {reasons}"
            : $"Фактический процесс существенно отличается от утверждённой НСИ: {reasons}";
    }
}
