namespace TechNormBlazor.Services.ProcessMining;

public enum ProcessOperator
{
    Activity,
    Sequence,
    ExclusiveChoice,
    Parallel,
    Loop,
    Fallback,
}

public class ProcessTreeNode
{
    public ProcessOperator Operator { get; set; }
    public string?              Activity { get; set; }
    public List<ProcessTreeNode> Children { get; set; } = [];

    public static ProcessTreeNode Leaf(string activity) =>
        new() { Operator = ProcessOperator.Activity, Activity = activity };

    public static ProcessTreeNode Make(ProcessOperator op, IEnumerable<ProcessTreeNode> children) =>
        new() { Operator = op, Children = [.. children] };
}

public class RouteVariant
{
    public int VariantNumber { get; set; }
    public List<string> Operations { get; set; } = [];
    public int TraceCount { get; set; }
    public decimal TraceShare { get; set; }
    public decimal AverageConfidence { get; set; }
    public bool HasRareTransitions { get; set; }
    public List<string> RareTransitions { get; set; } = [];
}

public class MiningMetrics
{
    public int     TotalTraceCount  { get; set; }
    public int     UsedTraceCount   { get; set; }
    public int     TotalEventCount  { get; set; }
    public int     UsedEventCount   { get; set; }
    public decimal Coverage         { get; set; }
    public int     VariantCount     { get; set; }
    public decimal MainVariantShare { get; set; }
    public int     RareTransitionCount        { get; set; }
    public decimal AverageTransitionConfidence { get; set; }
    public int     GeneratedSequentialRouteCount { get; set; }
    public bool    HasParallelBlocks { get; set; }
    public bool    HasChoiceBlocks   { get; set; }
    public bool    HasLoopBlocks     { get; set; }
    public string  MiningAlgorithm  { get; set; } = "InductiveMinerLike";
    public string  MiningNotes      { get; set; } = "";
}
