using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;

namespace TechNormBlazor.Services.Analytics;

public interface IAnalyticsService
{
    Task<ProductAnalyticsData?> GetProductAnalyticsAsync(int productId);
}

public class AnalyticsService(IDbContextFactory<TechNormDbContext> factory) : IAnalyticsService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public async Task<ProductAnalyticsData?> GetProductAnalyticsAsync(int productId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var product = await db.Products.FindAsync(productId);
        if (product is null) return null;

        var routes = await db.TechRoutes
            .Where(r => r.ProductId == productId)
            .Include(r => r.Steps)
                .ThenInclude(s => s.TimeNorms)
            .Include(r => r.Steps)
                .ThenInclude(s => s.MaterialNorms)
                    .ThenInclude(mn => mn.Material)
            .OrderByDescending(r => r.Version)
            .ToListAsync();

        var published = routes.FirstOrDefault(r => r.Status == "published");

        var eventCount = await db.EventLogs.CountAsync(e => e.ProductId == productId);
        var traceCount = await db.EventLogs
            .Where(e => e.ProductId == productId)
            .Select(e => e.CaseId)
            .Distinct()
            .CountAsync();

        var allSteps        = routes.SelectMany(r => r.Steps).ToList();
        var timeNormCount   = allSteps.SelectMany(s => s.TimeNorms).Count();
        var matNormCount    = allSteps.SelectMany(s => s.MaterialNorms).Count();
        var lastRouteUpdate = routes.Select(r => (DateTime?)r.UpdatedAt).Max();

        var histories = await db.CalculationHistories
            .Where(h => h.ProductId == productId)
            .OrderBy(h => h.CalculatedAt)
            .ToListAsync();

        var conformanceHistories = histories
            .Where(h => h.Metrics is not null)
            .Select(h => TryParseConformance(h.Metrics!))
            .Where(m => m is not null)
            .Select(m => m!)
            .ToList();

        var pciHistory = conformanceHistories
            .Where(m => m.CalculatedAt != default)
            .OrderBy(m => m.CalculatedAt)
            .Select(m => new PciHistoryPoint
            {
                Date    = m.CalculatedAt,
                Pci     = m.Pci,
                Status  = m.Status ?? "",
                Trigger = m.Trigger ?? "",
            })
            .ToList();

        var latestConf = conformanceHistories.OrderByDescending(m => m.CalculatedAt).FirstOrDefault();

        var topTimeDeviations = new List<OperationDeviationEntry>();
        var opIssues          = new List<OperationIssueEntry>();
        var transIssues       = new List<TransitionIssueEntry>();
        var matIssues         = new List<MaterialIssueEntry>();
        var resIssues         = new List<ResourceIssueEntry>();

        if (latestConf?.Issues is not null)
        {
            opIssues = (latestConf.Issues.Operations ?? [])
                .Select(i => new OperationIssueEntry
                {
                    OperationName    = i.OperationName ?? "",
                    NormTime         = i.NormTime,
                    ActualTime       = i.ActualTime,
                    DeviationPercent = i.DeviationPercent,
                    Severity         = i.Severity ?? "",
                    Message          = i.Message ?? "",
                })
                .ToList();

            topTimeDeviations = opIssues
                .OrderByDescending(i => i.DeviationPercent)
                .Take(8)
                .Select(i => new OperationDeviationEntry
                {
                    OperationName    = i.OperationName,
                    NormTime         = i.NormTime,
                    ActualTime       = i.ActualTime,
                    DeviationPercent = i.DeviationPercent,
                    Severity         = i.Severity,
                })
                .ToList();

            transIssues = (latestConf.Issues.Transitions ?? [])
                .Select(i => new TransitionIssueEntry
                {
                    FromOperation = i.FromOperation ?? "",
                    ToOperation   = i.ToOperation ?? "",
                    ActualCount   = i.ActualCount,
                    ExistsInRoute = i.ExistsInRoute,
                    Severity      = i.Severity ?? "",
                    Message       = i.Message ?? "",
                })
                .ToList();

            matIssues = (latestConf.Issues.Materials ?? [])
                .Select(i => new MaterialIssueEntry
                {
                    MaterialName      = i.MaterialName ?? "",
                    OperationName     = i.OperationName,
                    NormConsumption   = i.NormConsumption,
                    ActualConsumption = i.ActualConsumption,
                    DeviationPercent  = i.DeviationPercent,
                    Severity          = i.Severity ?? "",
                    Message           = i.Message ?? "",
                })
                .ToList();

            resIssues = (latestConf.Issues.Resources ?? [])
                .Select(i => new ResourceIssueEntry
                {
                    OperationName    = i.OperationName ?? "",
                    ResourceName     = i.ResourceName ?? "",
                    IsAllowedResource = i.IsAllowedResource,
                    UsageCount       = i.UsageCount,
                    Severity         = i.Severity ?? "",
                    Message          = i.Message ?? "",
                })
                .ToList();
        }

        var allPublishedRoutes = await db.TechRoutes
            .Where(r => r.Status == "published" && r.LastPci != null)
            .Include(r => r.Product)
            .ToListAsync();

        var allProductsPci = allPublishedRoutes
            .Where(r => r.Product is not null)
            .Select(r => new ProductPciEntry
            {
                ProductName = r.Product!.Name,
                Pci         = r.LastPci!.Value,
                Status      = r.LastPciStatus ?? "NoData",
                IsCurrent   = r.ProductId == productId,
            })
            .OrderBy(e => e.Pci)
            .ToList();

        var totalDocs     = await db.Documents.CountAsync();
        var totalEvents   = await db.EventLogs.CountAsync();
        var totalRoutes   = await db.TechRoutes.CountAsync();
        var totalTimeN    = await db.TimeNorms.CountAsync();
        var totalMatN     = await db.MaterialNorms.CountAsync();
        var nonConformant = await db.TechRoutes
            .CountAsync(r => r.LastPciStatus == "NonConformant");
        var needsReview   = await db.TechRoutes
            .CountAsync(r => r.LastPciStatus == "NeedsReview");

        return new ProductAnalyticsData
        {
            ProductId            = productId,
            ProductName          = product.Name,
            ProductCode          = product.Code,
            PublishedRouteName   = published?.Name,
            PublishedRouteVersion = published?.Version,
            CurrentPci           = published?.LastPci,
            PciStatus            = published?.LastPciStatus,
            PciCalculatedAt      = published?.LastPciCalculatedAt,
            PciSummary           = published?.LastPciSummary,
            EventCount           = eventCount,
            TraceCount           = traceCount,
            RouteCount           = routes.Count,
            TimeNormCount        = timeNormCount,
            MaterialNormCount    = matNormCount,
            LastRouteUpdated     = lastRouteUpdate,
            PciHistory           = pciHistory,
            AllProductsPci       = allProductsPci,
            TimeDeviationAvg     = latestConf?.TimeDeviationAvg ?? 0m,
            RouteDeviation       = latestConf?.RouteDeviation ?? 0m,
            MaterialDeviationAvg = latestConf?.MaterialDeviationAvg ?? 0m,
            ResourceDeviation    = latestConf?.ResourceDeviation ?? 0m,
            TopTimeDeviations    = topTimeDeviations,
            OperationIssues      = opIssues,
            TransitionIssues     = transIssues,
            MaterialIssues       = matIssues,
            ResourceIssues       = resIssues,
            Global               = new GlobalAnalyticsStats
            {
                TotalDocuments    = totalDocs,
                TotalEvents       = totalEvents,
                TotalRoutes       = totalRoutes,
                TotalTimeNorms    = totalTimeN,
                TotalMaterialNorms = totalMatN,
                RoutesNonConformant = nonConformant,
                RoutesNeedsReview  = needsReview,
            },
        };
    }

    private static ConformanceMetricsDoc? TryParseConformance(JsonDocument doc)
    {
        try
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return null;
            if (typeProp.GetString() != "ConformanceChecking") return null;
            return JsonSerializer.Deserialize<ConformanceMetricsDoc>(root.GetRawText(), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private sealed class ConformanceMetricsDoc
    {
        public decimal Pci { get; set; }
        public string? Status { get; set; }
        public string? Trigger { get; set; }
        public DateTime CalculatedAt { get; set; }
        public decimal TimeDeviationAvg { get; set; }
        public decimal RouteDeviation { get; set; }
        public decimal MaterialDeviationAvg { get; set; }
        public decimal ResourceDeviation { get; set; }
        public ConformanceIssuesDoc? Issues { get; set; }
    }

    private sealed class ConformanceIssuesDoc
    {
        public List<OperationIssueDoc>?  Operations { get; set; }
        public List<TransitionIssueDoc>? Transitions { get; set; }
        public List<MaterialIssueDoc>?   Materials { get; set; }
        public List<ResourceIssueDoc>?   Resources { get; set; }
    }

    private sealed class OperationIssueDoc
    {
        public string? OperationName { get; set; }
        public decimal? NormTime { get; set; }
        public decimal? ActualTime { get; set; }
        public decimal DeviationPercent { get; set; }
        public string? Severity { get; set; }
        public string? Message { get; set; }
    }

    private sealed class TransitionIssueDoc
    {
        public string? FromOperation { get; set; }
        public string? ToOperation { get; set; }
        public int ActualCount { get; set; }
        public bool ExistsInRoute { get; set; }
        public string? Severity { get; set; }
        public string? Message { get; set; }
    }

    private sealed class MaterialIssueDoc
    {
        public string? MaterialName { get; set; }
        public string? OperationName { get; set; }
        public decimal? NormConsumption { get; set; }
        public decimal? ActualConsumption { get; set; }
        public decimal DeviationPercent { get; set; }
        public string? Severity { get; set; }
        public string? Message { get; set; }
    }

    private sealed class ResourceIssueDoc
    {
        public string? OperationName { get; set; }
        public string? ResourceName { get; set; }
        public bool IsAllowedResource { get; set; }
        public int UsageCount { get; set; }
        public string? Severity { get; set; }
        public string? Message { get; set; }
    }
}
