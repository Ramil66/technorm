namespace TechNormBlazor.Services.Analytics;

public class ProductAnalyticsData
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductCode { get; set; }
    public string? PublishedRouteName { get; set; }
    public int? PublishedRouteVersion { get; set; }
    public decimal? CurrentPci { get; set; }
    public string? PciStatus { get; set; }
    public DateTime? PciCalculatedAt { get; set; }
    public string? PciSummary { get; set; }
    public int EventCount { get; set; }
    public int TraceCount { get; set; }
    public int RouteCount { get; set; }
    public int TimeNormCount { get; set; }
    public int MaterialNormCount { get; set; }
    public DateTime? LastRouteUpdated { get; set; }
    public List<PciHistoryPoint> PciHistory { get; set; } = [];
    public List<ProductPciEntry> AllProductsPci { get; set; } = [];
    public decimal TimeDeviationAvg { get; set; }
    public decimal RouteDeviation { get; set; }
    public decimal MaterialDeviationAvg { get; set; }
    public decimal ResourceDeviation { get; set; }
    public List<OperationDeviationEntry> TopTimeDeviations { get; set; } = [];
    public List<OperationIssueEntry> OperationIssues { get; set; } = [];
    public List<TransitionIssueEntry> TransitionIssues { get; set; } = [];
    public List<MaterialIssueEntry> MaterialIssues { get; set; } = [];
    public List<ResourceIssueEntry> ResourceIssues { get; set; } = [];
    public GlobalAnalyticsStats Global { get; set; } = new();
}

public class PciHistoryPoint
{
    public DateTime Date { get; set; }
    public decimal Pci { get; set; }
    public string Status { get; set; } = "";
    public string Trigger { get; set; } = "";
}

public class ProductPciEntry
{
    public string ProductName { get; set; } = "";
    public decimal Pci { get; set; }
    public string Status { get; set; } = "";
    public bool IsCurrent { get; set; }
}

public class OperationDeviationEntry
{
    public string OperationName { get; set; } = "";
    public decimal? NormTime { get; set; }
    public decimal? ActualTime { get; set; }
    public decimal DeviationPercent { get; set; }
    public string Severity { get; set; } = "";
}

public class OperationIssueEntry
{
    public string OperationName { get; set; } = "";
    public decimal? NormTime { get; set; }
    public decimal? ActualTime { get; set; }
    public decimal DeviationPercent { get; set; }
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}

public class TransitionIssueEntry
{
    public string FromOperation { get; set; } = "";
    public string ToOperation { get; set; } = "";
    public int ActualCount { get; set; }
    public bool ExistsInRoute { get; set; }
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}

public class MaterialIssueEntry
{
    public string MaterialName { get; set; } = "";
    public string? OperationName { get; set; }
    public decimal? NormConsumption { get; set; }
    public decimal? ActualConsumption { get; set; }
    public decimal DeviationPercent { get; set; }
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}

public class ResourceIssueEntry
{
    public string OperationName { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public bool IsAllowedResource { get; set; }
    public int UsageCount { get; set; }
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
}

public class GlobalAnalyticsStats
{
    public int TotalDocuments { get; set; }
    public int TotalEvents { get; set; }
    public int TotalRoutes { get; set; }
    public int TotalTimeNorms { get; set; }
    public int TotalMaterialNorms { get; set; }
    public int RoutesNonConformant { get; set; }
    public int RoutesNeedsReview { get; set; }
}
