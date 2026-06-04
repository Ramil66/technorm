namespace TechNormBlazor.Services.ConformanceChecking;

public class ConformanceCheckResult
{
    public int ProductId { get; set; }
    public int RouteId   { get; set; }

    public int TotalTraceCount   { get; set; }
    public int CheckedTraceCount { get; set; }
    public int TotalEventCount   { get; set; }

    public int ExpectedTransitionCount   { get; set; }
    public int ActualTransitionCount     { get; set; }
    public int MatchedTransitionCount    { get; set; }
    public int UnexpectedTransitionCount { get; set; }
    public int MissingTransitionCount    { get; set; }

    public decimal TimeDeviationAvg     { get; set; }
    public decimal MaterialDeviationAvg { get; set; }
    public decimal RouteDeviation       { get; set; }
    public decimal ResourceDeviation    { get; set; }

    public decimal ProcessConformanceIndex { get; set; }

    public string Status  { get; set; } = "NoData";
    public string Summary { get; set; } = "";

    public DateTime CalculatedAt { get; set; }
    public string Trigger { get; set; } = "manual";

    public List<OperationConformanceIssue>  OperationIssues  { get; set; } = [];
    public List<TransitionConformanceIssue> TransitionIssues { get; set; } = [];
    public List<MaterialConformanceIssue>   MaterialIssues   { get; set; } = [];
    public List<ResourceConformanceIssue>   ResourceIssues   { get; set; } = [];
}

public class OperationConformanceIssue
{
    public string   OperationName    { get; set; } = "";
    public decimal? NormTime         { get; set; }
    public decimal? ActualTime       { get; set; }
    public decimal  DeviationPercent { get; set; }
    public string   Severity         { get; set; } = "";
    public string   Message          { get; set; } = "";
}

public class TransitionConformanceIssue
{
    public string FromOperation { get; set; } = "";
    public string ToOperation   { get; set; } = "";
    public int    ActualCount   { get; set; }
    public bool   ExistsInRoute { get; set; }
    public string Severity      { get; set; } = "";
    public string Message       { get; set; } = "";
}

public class MaterialConformanceIssue
{
    public string   MaterialName      { get; set; } = "";
    public string?  OperationName     { get; set; }
    public decimal? NormConsumption   { get; set; }
    public decimal? ActualConsumption { get; set; }
    public decimal  DeviationPercent  { get; set; }
    public string   Severity          { get; set; } = "";
    public string   Message           { get; set; } = "";
}

public class ResourceConformanceIssue
{
    public string  OperationName     { get; set; } = "";
    public string  ResourceName      { get; set; } = "";
    public bool    IsAllowedResource { get; set; }
    public int     UsageCount        { get; set; }
    public decimal UsageShare        { get; set; }
    public string  Severity          { get; set; } = "";
    public string  Message           { get; set; } = "";
}
