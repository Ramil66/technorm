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

    private const decimal SeverityMatHigh   = 15m;
    private const decimal SeverityMatMedium = 10m;

    private const decimal ThresholdConformant  = 85m;
    private const decimal ThresholdNeedsReview = 70m;

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

        var events = await db.EventLogs
            .Where(e => e.ProductId == productId)
            .Include(e => e.Resource)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct);

        if (events.Count == 0)
        {
            logger.LogInformation(
                "ConformanceCheck: no events for product {ProductId}", productId);
            return null;
        }

        var orderedSteps = route.Steps.OrderBy(s => s.SequenceNum).ToList();

        var traces = BuildActualTraces(events);

        var expectedTransitions = BuildExpectedTransitions(orderedSteps);

        var actualTransitions = BuildActualTransitions(traces);

        var transResult  = CheckTransitions(expectedTransitions, actualTransitions);

        var timeResult   = CheckTimeNorms(orderedSteps, events);

        var matResult    = CheckMaterialNorms(orderedSteps, events);

        var resResult    = CheckResources(orderedSteps, events);

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
            ProductId                = productId,
            RouteId                  = route.Id,
            TotalTraceCount          = traces.Count,
            CheckedTraceCount        = traces.Count,
            TotalEventCount          = events.Count,
            ExpectedTransitionCount  = transResult.ExpectedCount,
            ActualTransitionCount    = transResult.ActualCount,
            MatchedTransitionCount   = transResult.MatchedCount,
            UnexpectedTransitionCount = transResult.UnexpectedCount,
            MissingTransitionCount   = transResult.MissingCount,
            TimeDeviationAvg         = timeResult.Avg,
            MaterialDeviationAvg     = matResult.Avg,
            RouteDeviation           = transResult.RouteDeviation,
            ResourceDeviation        = resResult.Deviation,
            ProcessConformanceIndex  = pci,
            Status                   = status,
            Summary                  = summary,
            CalculatedAt             = DateTime.UtcNow,
            Trigger                  = trigger,
            OperationIssues          = timeResult.Issues,
            TransitionIssues         = transResult.Issues,
            MaterialIssues           = matResult.Issues,
            ResourceIssues           = resResult.Issues,
        };

        await SaveConformanceMetricsAsync(db, result, ct);

        await UpdateRoutePciStatusAsync(db, route.Id, result, ct);

        return result;
    }

    private static Dictionary<string, List<EventLog>> BuildActualTraces(List<EventLog> events)
        => events
            .GroupBy(e => e.CaseId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.Timestamp).ToList());

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

    private static Dictionary<(string From, string To), int> BuildActualTransitions(
        Dictionary<string, List<EventLog>> traces)
    {
        var result = new Dictionary<(string, string), int>();
        foreach (var trace in traces.Values)
        {
            var seq = trace.Select(e => e.Activity)
                           .Where(a => !string.IsNullOrWhiteSpace(a))
                           .ToList();
            for (int i = 0; i < seq.Count - 1; i++)
            {
                if (seq[i] == seq[i + 1]) continue;
                var key = (seq[i], seq[i + 1]);
                result[key] = result.GetValueOrDefault(key) + 1;
            }
        }
        return result;
    }

    private record TransitionCheckResult(
        int ExpectedCount, int ActualCount, int MatchedCount,
        int UnexpectedCount, int MissingCount, decimal RouteDeviation,
        List<TransitionConformanceIssue> Issues);

    private static TransitionCheckResult CheckTransitions(
        HashSet<(string From, string To)> expected,
        Dictionary<(string From, string To), int> actual)
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
            var normKey = (Norm(key.From), Norm(key.To));
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
                    FromOperation = key.From,
                    ToOperation   = key.To,
                    ActualCount   = count,
                    ExistsInRoute = false,
                    Severity      = count <= 2 ? "Low" : "High",
                    Message       = $"Переход «{key.From} → {key.To}» ({count}×) отсутствует в утверждённом маршруте.",
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

        int total      = Math.Max(expected.Count + actual.Count, 1);
        decimal dev    = Math.Round((decimal)(unexpected + missing) / total * 100m, 2);

        return new TransitionCheckResult(
            expected.Count, actual.Count, matched,
            unexpected, missing, dev, issues);
    }

    private record TimeCheckResult(decimal Avg, bool HasData, List<OperationConformanceIssue> Issues);

    private static TimeCheckResult CheckTimeNorms(
        List<RouteStep> steps,
        List<EventLog>  events)
    {
        var issues       = new List<OperationConformanceIssue>();
        decimal weightedSum  = 0m;
        int     totalSamples = 0;
        bool    hasData      = false;

        foreach (var step in steps)
        {
            string opName = StepName(step);
            decimal? norm = BestNorm(step);
            if (norm is null or 0) continue;

            var durations = events
                .Where(e => NamesMatch(e.Activity, opName) && e.Duration.HasValue)
                .Select(e => (decimal)e.Duration!.Value.TotalMinutes)
                .ToList();

            if (durations.Count == 0) continue;

            hasData = true;
            decimal median = Median(durations);
            decimal dev    = norm.Value > 0
                ? Math.Round(Math.Abs(median - norm.Value) / norm.Value * 100m, 2)
                : 0m;

            weightedSum  += dev * durations.Count;
            totalSamples += durations.Count;

            if (dev >= SeverityTimeMedium)
                issues.Add(new OperationConformanceIssue
                {
                    OperationName   = opName,
                    NormTime        = norm,
                    ActualTime      = Math.Round(median, 2),
                    DeviationPercent = dev,
                    Severity        = dev >= SeverityTimeHigh ? "High" : "Medium",
                    Message         = $"Операция «{opName}»: факт {median:F1} мин, норма {norm:F1} мин, отклонение {dev:F1}%.",
                });
        }

        decimal avg = totalSamples > 0
            ? Math.Round(weightedSum / totalSamples, 2)
            : 0m;

        return new TimeCheckResult(avg, hasData, issues);
    }

    private record MaterialCheckResult(decimal Avg, bool HasData, List<MaterialConformanceIssue> Issues);

    private static MaterialCheckResult CheckMaterialNorms(
        List<RouteStep> steps,
        List<EventLog>  events)
    {
        var issues = new List<MaterialConformanceIssue>();

        var factualMaterials = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);
        foreach (var evt in events)
        {
            if (evt.Metadata is null) continue;
            try
            {
                var root = evt.Metadata.RootElement;
                if (!root.TryGetProperty("materials", out var matArray)) continue;
                foreach (var item in matArray.EnumerateArray())
                {
                    string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    decimal qty = item.TryGetProperty("quantity", out var q) && q.TryGetDecimal(out var qd) ? qd : 0m;
                    if (string.IsNullOrWhiteSpace(name) || qty <= 0) continue;
                    factualMaterials.TryAdd(name, []);
                    factualMaterials[name].Add(qty);
                }
            }
            catch {  }
        }

        if (factualMaterials.Count == 0)
            return new MaterialCheckResult(0m, false, issues);

        decimal totalDev = 0m;
        int     count    = 0;

        foreach (var step in steps)
        {
            string opName = StepName(step);
            foreach (var mn in step.MaterialNorms)
            {
                string matName = mn.Material?.Name ?? $"Материал {mn.MaterialId}";
                decimal norm   = mn.ConsumptionRate;
                if (norm <= 0) continue;

                if (!factualMaterials.TryGetValue(matName, out var quantities)) continue;

                decimal median = Median(quantities);
                decimal dev    = Math.Round(Math.Abs(median - norm) / norm * 100m, 2);

                totalDev += dev;
                count++;

                if (dev >= SeverityMatMedium)
                    issues.Add(new MaterialConformanceIssue
                    {
                        MaterialName     = matName,
                        OperationName    = opName,
                        NormConsumption  = norm,
                        ActualConsumption = Math.Round(median, 4),
                        DeviationPercent = dev,
                        Severity         = dev >= SeverityMatHigh ? "High" : "Medium",
                        Message          = $"Материал «{matName}» ({opName}): факт {median:F3}, норма {norm:F3}, отклонение {dev:F1}%.",
                    });
            }
        }

        decimal avg = count > 0 ? Math.Round(totalDev / count, 2) : 0m;
        return new MaterialCheckResult(avg, count > 0, issues);
    }

    private record ResourceCheckResult(decimal Deviation, bool HasData, List<ResourceConformanceIssue> Issues);

    private static ResourceCheckResult CheckResources(
        List<RouteStep> steps,
        List<EventLog>  events)
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

        var usageGroups = events
            .Where(e => !string.IsNullOrWhiteSpace(e.Activity) && e.ResourceId.HasValue)
            .GroupBy(e => (e.Activity, e.ResourceId!.Value));

        foreach (var grp in usageGroups)
        {
            string activity   = grp.Key.Activity;
            int    resourceId = grp.Key.Value;
            string resName    = grp.First().Resource?.Name ?? $"Ресурс {resourceId}";
            int    cnt        = grp.Count();

            bool opInRoute = allowedResources.TryGetValue(activity, out var allowed);
            if (!opInRoute) continue;

            totalUsages += cnt;

            if (!allowed!.Contains(resourceId))
            {
                invalidUsages += cnt;
                decimal share = Math.Round(cnt * 100m / Math.Max(cnt, 1), 2);
                issues.Add(new ResourceConformanceIssue
                {
                    OperationName    = activity,
                    ResourceName     = resName,
                    IsAllowedResource = false,
                    UsageCount       = cnt,
                    UsageShare       = share,
                    Severity         = "High",
                    Message          = $"Операция «{activity}»: ресурс «{resName}» не допустим согласно маршруту ({cnt}×).",
                });
            }
        }

        bool hasData    = totalUsages > 0;
        decimal dev     = hasData
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
        decimal wTime = hasTimeData     ? WeightTime     : 0m;
        decimal wMat  = hasMaterialData ? WeightMaterial : 0m;
        decimal wRes  = hasResourceData ? WeightResource : 0m;
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
            type                     = "ConformanceChecking",
            productId                = result.ProductId,
            routeId                  = result.RouteId,
            pci                      = result.ProcessConformanceIndex,
            status                   = result.Status,
            summary                  = result.Summary,
            trigger                  = result.Trigger,
            timeDeviationAvg         = result.TimeDeviationAvg,
            materialDeviationAvg     = result.MaterialDeviationAvg,
            routeDeviation           = result.RouteDeviation,
            resourceDeviation        = result.ResourceDeviation,
            totalTraceCount          = result.TotalTraceCount,
            checkedTraceCount        = result.CheckedTraceCount,
            totalEventCount          = result.TotalEventCount,
            expectedTransitionCount  = result.ExpectedTransitionCount,
            actualTransitionCount    = result.ActualTransitionCount,
            matchedTransitionCount   = result.MatchedTransitionCount,
            unexpectedTransitionCount = result.UnexpectedTransitionCount,
            missingTransitionCount   = result.MissingTransitionCount,
            calculatedAt             = result.CalculatedAt,
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

    private static bool NamesMatch(string actual, string expected) =>
        string.Equals(actual?.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string s) => s?.Trim().ToLowerInvariant() ?? "";

    private static decimal? BestNorm(RouteStep step)
    {
        var norms = step.TimeNorms.Where(t => t.NormValue > 0).Select(t => t.NormValue).ToList();
        if (norms.Count == 0) return null;
        return norms.Average();
    }

    private static decimal Median(List<decimal> values)
    {
        if (values.Count == 0) return 0m;
        var sorted = values.OrderBy(v => v).ToList();
        int mid    = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2m
            : sorted[mid];
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
