using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;
using TechNormBlazor.Services.ProcessMining;

namespace TechNormBlazor.Services;

public class MinerResourceInfo
{
    public string  ResourceName   { get; set; } = "";
    public decimal AvgDurationMin { get; set; }
    public int     SampleCount    { get; set; }
}

public class MinerActivityInfo
{
    public string Name { get; set; } = "";
    public int TotalOccurrences { get; set; }
    public int CaseCount        { get; set; }
    public decimal Confidence       { get; set; }
    public decimal AvgDurationMin   { get; set; }
    public decimal MinDurationMin   { get; set; }
    public decimal MaxDurationMin   { get; set; }
    public List<string>           Resources    { get; set; } = [];
    public List<MinerResourceInfo> ResourceStats { get; set; } = [];
}

public class MinerTransitionInfo
{
    public string  From       { get; set; } = "";
    public string  To         { get; set; } = "";
    public int     Count      { get; set; }
    public decimal Confidence { get; set; }
    public bool    IsRare     { get; set; }
}

public class MinerResult
{
    public bool    Success      { get; set; }
    public string? ErrorMessage { get; set; }
    public int     TotalCases   { get; set; }
    public int     TotalEvents  { get; set; }
    public List<MinerActivityInfo>   Activities          { get; set; } = [];
    public List<List<string>>        Stages              { get; set; } = [];
    public decimal                   OverallConfidence   { get; set; }
    public decimal                   Coverage            { get; set; }
    public int                       RareTransitionCount { get; set; }
    public List<MinerTransitionInfo> Transitions         { get; set; } = [];
    public List<RouteVariant>        RouteVariants { get; set; } = [];
    public MiningMetrics             Metrics       { get; set; } = new();
}

public interface IRouteBuilderService
{
    Task<MinerResult>       MineFromEventsAsync(int productId);
    Task<List<TechRoute>>   BuildRouteFromMinerAsync(
        MinerResult result, int productId, string routeName, int userId);
    Task AutoUpdateIfEnabledAsync(int productId, int userId);
    Task ManualRebuildAsync(int routeId, int userId);
}

public class RouteBuilderService(
    IDbContextFactory<TechNormDbContext> factory,
    ITechRouteService                   routeSvc,
    IRouteStepService                   stepSvc,
    ITimeNormService                    timeNormSvc,
    IOperationMatcherService            matcherSvc,
    IConformanceCheckingService         conformanceSvc) : IRouteBuilderService
{
    private const decimal RareTransitionThreshold = 10m;
    private const int     MaxRouteVariants        = 10;
    private const int     MaxMiningDepth          = 20;

    public async Task<MinerResult> MineFromEventsAsync(int productId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var events = await db.EventLogs
            .Where(e => e.ProductId == productId)
            .Include(e => e.Resource)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

        if (events.Count == 0)
            return new MinerResult
            {
                Success      = false,
                ErrorMessage = "Для данного изделия в журнале событий записей нет",
            };

        var validEvents = events.Where(e => !string.IsNullOrWhiteSpace(e.Activity)).ToList();

        if (validEvents.Count == 0)
            return new MinerResult
            {
                Success      = false,
                ErrorMessage = "Нет событий с заполненной активностью для построения маршрута",
            };

        var rawTraces = validEvents
            .GroupBy(e => e.CaseId)
            .Select(g => g.OrderBy(e => e.Timestamp).ToList())
            .ToList();

        int totalCases  = rawTraces.Count;
        int totalEvents = events.Count;
        int usedEvents  = validEvents.Count;

        var traces = rawTraces
            .Select(t => NormalizeTrace(t.Select(e => e.Activity).ToList()))
            .Where(t => t.Count > 0)
            .ToList();

        int usedTraces = traces.Count;

        var actCases     = new Dictionary<string, HashSet<string>>();
        var actDurations = new Dictionary<string, List<decimal>>();
        var actResources = new Dictionary<string, HashSet<string>>();
        var actResDurs   = new Dictionary<(string Act, string Res), List<decimal>>();

        foreach (var rawTrace in rawTraces)
        {
            var seenDur = new HashSet<string>();
            foreach (var evt in rawTrace)
            {
                var act = evt.Activity;
                actCases.TryAdd(act, []);
                actResources.TryAdd(act, []);
                actCases[act].Add(evt.CaseId);

                if (evt.Resource is not null)
                    actResources[act].Add(evt.Resource.Name);

                if (evt.Duration.HasValue)
                {
                    var mins = (decimal)evt.Duration.Value.TotalMinutes;
                    if (seenDur.Add(act))
                    {
                        actDurations.TryAdd(act, []);
                        actDurations[act].Add(mins);
                    }
                    if (evt.Resource is not null)
                    {
                        var key = (act, evt.Resource.Name);
                        actResDurs.TryAdd(key, []);
                        actResDurs[key].Add(mins);
                    }
                }
            }
        }

        var dfg      = new Dictionary<(string From, string To), int>();
        var startCnt = new Dictionary<string, int>();
        var endCnt   = new Dictionary<string, int>();

        foreach (var trace in traces)
        {
            if (trace.Count == 0) continue;
            startCnt[trace[0]]   = startCnt.GetValueOrDefault(trace[0])   + 1;
            endCnt[trace[^1]]    = endCnt.GetValueOrDefault(trace[^1])    + 1;

            for (int i = 0; i < trace.Count - 1; i++)
            {
                var key = (trace[i], trace[i + 1]);
                dfg[key] = dfg.GetValueOrDefault(key) + 1;
            }
        }

        var outTotals = dfg
            .GroupBy(kv => kv.Key.From)
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));

        var transitionConf = dfg.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var total = outTotals.GetValueOrDefault(kv.Key.From, 0);
                return total > 0 ? Math.Round(kv.Value * 100m / total, 1) : 0m;
            });

        var transitions = transitionConf
            .Select(kv => new MinerTransitionInfo
            {
                From       = kv.Key.From,
                To         = kv.Key.To,
                Count      = dfg[kv.Key],
                Confidence = kv.Value,
                IsRare     = kv.Value < RareTransitionThreshold,
            })
            .OrderBy(t => t.From)
            .ThenByDescending(t => t.Count)
            .ThenBy(t => t.To)
            .ToList();

        int rareTransitionCount = transitions.Count(t => t.IsRare);

        var tree = MineProcessTree(traces, 0);

        var allSequences = FlattenToSequences(tree);
        var sequences    = allSequences.Distinct(new SequenceComparer()).Take(MaxRouteVariants).ToList();

        var routeVariants = BuildRouteVariants(sequences, traces, transitionConf);

        var stages = ExtractStagesFromTree(tree);

        var allActs   = stages.SelectMany(s => s).Distinct().ToList();
        var actInfos  = allActs.Select(act =>
        {
            var durs      = actDurations.GetValueOrDefault(act) ?? [];
            var caseCount = actCases.GetValueOrDefault(act)?.Count ?? 0;

            var resSats = actResDurs
                .Where(kv => kv.Key.Act == act && kv.Value.Count > 0)
                .Select(kv => new MinerResourceInfo
                {
                    ResourceName   = kv.Key.Res,
                    AvgDurationMin = Math.Round(kv.Value.Average(), 2),
                    SampleCount    = kv.Value.Count,
                })
                .OrderByDescending(r => r.AvgDurationMin)
                .ToList();

            return new MinerActivityInfo
            {
                Name             = act,
                TotalOccurrences = validEvents.Count(e => e.Activity == act),
                CaseCount        = caseCount,
                Confidence       = totalCases > 0
                                   ? Math.Round(caseCount * 100m / totalCases, 1)
                                   : 0m,
                AvgDurationMin   = durs.Count > 0 ? Math.Round(durs.Average(), 2) : 0m,
                MinDurationMin   = durs.Count > 0 ? Math.Round(durs.Min(),     2) : 0m,
                MaxDurationMin   = durs.Count > 0 ? Math.Round(durs.Max(),     2) : 0m,
                Resources        = [.. (actResources.GetValueOrDefault(act) ?? [])],
                ResourceStats    = resSats,
            };
        }).ToList();

        decimal overallConf = actInfos.Count > 0
            ? Math.Round(actInfos.Average(a => a.Confidence), 1)
            : 0m;

        decimal coverage = Math.Round(usedEvents * 100m / totalEvents, 1);

        bool hasParallel = ContainsOperator(tree, ProcessOperator.Parallel);
        bool hasChoice   = ContainsOperator(tree, ProcessOperator.ExclusiveChoice);
        bool hasLoop     = ContainsOperator(tree, ProcessOperator.Loop);

        var mainVariant    = routeVariants.FirstOrDefault();
        var mainShare      = usedTraces > 0 && mainVariant is not null
                             ? Math.Round(mainVariant.TraceCount * 100m / usedTraces, 1)
                             : 0m;
        var avgConfidence  = transitions.Count > 0
                             ? Math.Round(transitions.Average(t => t.Confidence), 1)
                             : 0m;
        var miningNotes    = new List<string>();
        if (ContainsOperator(tree, ProcessOperator.Fallback))
            miningNotes.Add("использован fallback для части процесса");
        if (hasLoop)
            miningNotes.Add("обнаружены циклические участки, loop развёрнут в последовательные варианты");
        if (hasParallel)
            miningNotes.Add("параллельные блоки развёрнуты в несколько вариантов маршрута");

        var metrics = new MiningMetrics
        {
            TotalTraceCount              = totalCases,
            UsedTraceCount               = usedTraces,
            TotalEventCount              = totalEvents,
            UsedEventCount               = usedEvents,
            Coverage                     = coverage,
            VariantCount                 = routeVariants.Count,
            MainVariantShare             = mainShare,
            RareTransitionCount          = rareTransitionCount,
            AverageTransitionConfidence  = avgConfidence,
            GeneratedSequentialRouteCount = sequences.Count,
            HasParallelBlocks            = hasParallel,
            HasChoiceBlocks              = hasChoice,
            HasLoopBlocks                = hasLoop,
            MiningAlgorithm              = "InductiveMinerLike",
            MiningNotes                  = string.Join("; ", miningNotes),
        };

        return new MinerResult
        {
            Success             = true,
            TotalCases          = totalCases,
            TotalEvents         = totalEvents,
            Activities          = actInfos,
            Stages              = stages,
            OverallConfidence   = overallConf,
            Coverage            = coverage,
            RareTransitionCount = rareTransitionCount,
            Transitions         = transitions,
            RouteVariants       = routeVariants,
            Metrics             = metrics,
        };
    }

    public async Task<List<TechRoute>> BuildRouteFromMinerAsync(
        MinerResult result, int productId, string routeName, int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var resources = await db.Resources.ToListAsync();
        var actLookup = result.Activities.ToDictionary(a => a.Name);

        var paths = CartesianPaths(result.Stages).ToList();

        var createdRoutes = new List<TechRoute>();
        for (int pi = 0; pi < paths.Count; pi++)
        {
            var path = paths[pi];
            var name = pi == 0 ? routeName : $"{routeName} (вариант {pi + 1})";

            var route = new TechRoute
            {
                ProductId        = productId,
                Name             = name,
                Status           = "draft",
                Version          = 1,
                CreatedBy        = userId,
                SourceEventCount = result.TotalEvents,
            };
            route = await routeSvc.CreateAsync(route);

            int seqNum = 0;
            foreach (var actName in path)
            {
                seqNum++;
                var act   = actLookup.GetValueOrDefault(actName);
                var match = await matcherSvc.MatchAsync(actName);
                var op    = match.Status != MatchStatus.Unmatched ? match.Operation : null;

                var step = await stepSvc.CreateAsync(new RouteStep
                {
                    RouteId     = route.Id,
                    SequenceNum = seqNum,
                    OperationId = op?.Id,
                    Description = op is null ? actName : null,
                });

                if (act is not null)
                    await AddTimeNormsAsync(step.Id, act, resources);

                if (op is not null)
                    await CopyMaterialNormsAsync(db, step.Id, op.Id, route.Id);
            }
            createdRoutes.Add(route);
        }
        return createdRoutes;
    }

    public async Task AutoUpdateIfEnabledAsync(int productId, int userId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var route = await db.TechRoutes.FirstOrDefaultAsync(r =>
            r.ProductId == productId &&
            r.Status    == "published" &&
            r.IsAutoUpdate &&
            r.SourceEventCount != null);
        if (route is null) return;

        var result = await MineFromEventsAsync(productId);
        if (!result.Success) return;

        await RebuildStepsAsync(db, route.Id, result, userId);

        await db.TechRoutes.Where(r => r.Id == route.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SourceEventCount, result.TotalEvents)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));

        try { await conformanceSvc.TriggerRecalculationAsync(productId); } catch { }
    }

    public async Task ManualRebuildAsync(int routeId, int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var route = await db.TechRoutes.FindAsync(routeId);
        if (route is null || !route.SourceEventCount.HasValue) return;

        var result = await MineFromEventsAsync(route.ProductId);
        if (!result.Success) return;

        await RebuildStepsAsync(db, routeId, result, userId);

        await db.TechRoutes.Where(r => r.Id == routeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SourceEventCount, result.TotalEvents)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));

        try { await conformanceSvc.TriggerRecalculationAsync(route.ProductId); } catch { }
    }

    private async Task RebuildStepsAsync(
        TechNormDbContext db, int routeId, MinerResult result, int userId)
    {
        var stepIds = await db.RouteSteps
            .Where(s => s.RouteId == routeId)
            .Select(s => s.Id)
            .ToListAsync();

        await db.TimeNorms.Where(t => stepIds.Contains(t.RouteStepId)).ExecuteDeleteAsync();
        await db.MaterialNorms.Where(m => stepIds.Contains(m.RouteStepId)).ExecuteDeleteAsync();
        await db.RouteSteps.Where(s => s.RouteId == routeId).ExecuteDeleteAsync();

        var resources = await db.Resources.ToListAsync();
        var actLookup = result.Activities.ToDictionary(a => a.Name);

        int seqNum = 0;
        foreach (var stage in result.Stages)
        {
            seqNum++;
            var actName = stage[0];
            var act     = actLookup.GetValueOrDefault(actName);
            var match   = await matcherSvc.MatchAsync(actName);
            var op      = match.Status != MatchStatus.Unmatched ? match.Operation : null;

            var step = await stepSvc.CreateAsync(new RouteStep
            {
                RouteId     = routeId,
                SequenceNum = seqNum,
                OperationId = op?.Id,
                Description = op is null ? actName : null,
            });

            if (act is not null)
                await AddTimeNormsAsync(step.Id, act, resources);

            if (op is not null)
                await CopyMaterialNormsAsync(db, step.Id, op.Id, routeId);
        }
    }

    private static async Task CopyMaterialNormsAsync(
        TechNormDbContext db, int stepId, int operationId, int skipRouteId)
    {
        var src = await db.MaterialNorms
            .Where(mn => mn.RouteStep.OperationId == operationId
                      && mn.RouteStep.RouteId     != skipRouteId)
            .GroupBy(mn => mn.MaterialId)
            .Select(g => g.OrderByDescending(mn => mn.UpdatedAt).First())
            .ToListAsync();

        foreach (var s in src)
            db.MaterialNorms.Add(new MaterialNorm
            {
                RouteStepId     = stepId,
                MaterialId      = s.MaterialId,
                ConsumptionRate = s.ConsumptionRate,
                UpdatedAt       = DateTime.UtcNow,
            });

        if (src.Count > 0)
            await db.SaveChangesAsync();
    }

    private async Task AddTimeNormsAsync(int stepId, MinerActivityInfo act, List<Resource> resources)
    {
        if (act.ResourceStats.Any())
        {
            foreach (var rs in act.ResourceStats)
            {
                var resource = resources.FirstOrDefault(r =>
                    string.Equals(r.Name, rs.ResourceName, StringComparison.OrdinalIgnoreCase));
                if (resource is not null && rs.AvgDurationMin > 0)
                    await timeNormSvc.UpsertAsync(new TimeNorm
                    {
                        RouteStepId = stepId, ResourceId = resource.Id,
                        NormValue   = rs.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
                    });
            }
        }
        else if (act.AvgDurationMin > 0)
        {
            await timeNormSvc.UpsertAsync(new TimeNorm
            {
                RouteStepId = stepId, ResourceId = null,
                NormValue   = act.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
            });
        }
    }

    private static ProcessTreeNode MineProcessTree(List<List<string>> traces, int depth)
    {
        var activities = traces.SelectMany(t => t).Distinct().ToList();

        if (activities.Count == 0)
            return ProcessTreeNode.Make(ProcessOperator.Sequence, []);

        if (activities.Count == 1)
            return ProcessTreeNode.Leaf(activities[0]);

        if (depth >= MaxMiningDepth)
            return BuildFallback(activities, BuildDfg(traces));

        var dfg = BuildDfg(traces);

        var seqGroups = TrySequenceCut(activities, dfg);
        if (seqGroups is { Count: > 1 })
        {
            var children = seqGroups.Select(g =>
                MineProcessTree(ProjectTraces(traces, g), depth + 1));
            return ProcessTreeNode.Make(ProcessOperator.Sequence, children);
        }

        var xorGroups = TryXorCut(activities, traces);
        if (xorGroups is { Count: > 1 })
        {
            var children = xorGroups.Select(g =>
                MineProcessTree(FilterTracesForGroup(traces, g), depth + 1));
            return ProcessTreeNode.Make(ProcessOperator.ExclusiveChoice, children);
        }

        var loopParts = TryLoopCut(activities, traces, dfg);
        if (loopParts is not null)
        {
            var bodyNode = MineProcessTree(ProjectTraces(traces, loopParts.Value.Body), depth + 1);
            var redoNode = MineProcessTree(ProjectTraces(traces, loopParts.Value.Redo), depth + 1);
            return ProcessTreeNode.Make(ProcessOperator.Loop, [bodyNode, redoNode]);
        }

        var parGroups = TryParallelCut(activities, dfg);
        if (parGroups is not null)
        {
            if (parGroups.Count == 1)
            {
                var leafChildren = parGroups[0].Select(ProcessTreeNode.Leaf);
                return ProcessTreeNode.Make(ProcessOperator.Parallel, leafChildren);
            }
            var children = parGroups.Select(g =>
                MineProcessTree(ProjectTraces(traces, g), depth + 1));
            return ProcessTreeNode.Make(ProcessOperator.Parallel, children);
        }

        return BuildFallback(activities, dfg);
    }

    private static List<List<string>>? TrySequenceCut(
        List<string> activities,
        Dictionary<(string, string), int> dfg)
    {
        var actSet = new HashSet<string>(activities);
        var restrictedDfg = dfg
            .Where(kv => actSet.Contains(kv.Key.Item1) && actSet.Contains(kv.Key.Item2))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var sccs = FindSCCs(activities, restrictedDfg);
        if (sccs.Count <= 1) return null;

        var sccOf = new Dictionary<string, int>();
        for (int i = 0; i < sccs.Count; i++)
            foreach (var a in sccs[i])
                sccOf[a] = i;

        var inDeg = new int[sccs.Count];
        foreach (var kv in restrictedDfg)
        {
            int si = sccOf[kv.Key.Item1], ti = sccOf[kv.Key.Item2];
            if (si != ti) inDeg[ti]++;
        }

        var s1 = new List<string>();
        var s2 = new List<string>();
        for (int i = 0; i < sccs.Count; i++)
        {
            if (inDeg[i] == 0) s1.AddRange(sccs[i]);
            else               s2.AddRange(sccs[i]);
        }

        if (s1.Count == 0 || s2.Count == 0) return null;

        return [s1, s2];
    }

    private static List<List<string>>? TryXorCut(
        List<string> activities,
        List<List<string>> traces)
    {
        var actSet = new HashSet<string>(activities);
        var adj    = activities.ToDictionary(a => a, _ => new HashSet<string>());

        foreach (var trace in traces)
        {
            var present = trace.Where(a => actSet.Contains(a)).Distinct().ToList();
            for (int i = 0; i < present.Count; i++)
                for (int j = i + 1; j < present.Count; j++)
                {
                    adj[present[i]].Add(present[j]);
                    adj[present[j]].Add(present[i]);
                }
        }

        var components = FindConnectedComponents(activities, adj);
        return components.Count > 1 ? components : null;
    }

    private static (HashSet<string> Body, HashSet<string> Redo)? TryLoopCut(
        List<string> activities,
        List<List<string>> traces,
        Dictionary<(string, string), int> dfg)
    {
        var actSet = new HashSet<string>(activities);

        bool hasRepeat = traces.Any(t =>
            t.Where(a => actSet.Contains(a))
             .GroupBy(a => a)
             .Any(g => g.Count() > 1));

        if (!hasRepeat) return null;

        var bodyActs = new HashSet<string>();
        foreach (var trace in traces)
        {
            var first = trace.FirstOrDefault(a => actSet.Contains(a));
            if (first is not null) bodyActs.Add(first);
        }

        if (bodyActs.Count == 0) return null;

        var redoActs = new HashSet<string>();
        foreach (var a in actSet)
        {
            if (bodyActs.Contains(a)) continue;
            if (bodyActs.Any(b => dfg.ContainsKey((a, b))))
                redoActs.Add(a);
        }

        if (redoActs.Count == 0) return null;

        bodyActs.ExceptWith(redoActs);
        if (bodyActs.Count == 0) return null;

        return (bodyActs, redoActs);
    }

    private static List<List<string>>? TryParallelCut(
        List<string> activities,
        Dictionary<(string, string), int> dfg)
    {
        var actSet = new HashSet<string>(activities);
        var adj    = activities.ToDictionary(a => a, _ => new HashSet<string>());

        foreach (var a in activities)
            foreach (var b in activities)
                if (a != b && dfg.ContainsKey((a, b)) && dfg.ContainsKey((b, a)))
                {
                    adj[a].Add(b);
                    adj[b].Add(a);
                }

        if (adj.All(kv => kv.Value.Count == 0)) return null;

        var components = FindConnectedComponents(activities, adj);

        var validComponents = components
            .Where(c => c.Count > 1 || adj[c[0]].Count > 0)
            .ToList();

        return validComponents.Count >= 1 ? validComponents : null;
    }

    private static ProcessTreeNode BuildFallback(
        List<string> activities,
        Dictionary<(string, string), int> dfg)
    {
        if (activities.Count == 0)
            return ProcessTreeNode.Make(ProcessOperator.Fallback, []);

        var remaining = new HashSet<string>(activities);
        var ordered   = new List<string>();

        var inSum  = activities.ToDictionary(a => a, _ => 0);
        var outSum = activities.ToDictionary(a => a, _ => 0);
        foreach (var kv in dfg)
        {
            if (remaining.Contains(kv.Key.Item1)) outSum[kv.Key.Item1] += kv.Value;
            if (remaining.Contains(kv.Key.Item2)) inSum[kv.Key.Item2]  += kv.Value;
        }

        string? start = activities
            .OrderByDescending(a => outSum[a] - inSum[a])
            .ThenByDescending(a => outSum[a])
            .First();

        var current = start;
        while (current is not null && remaining.Contains(current))
        {
            ordered.Add(current);
            remaining.Remove(current);

            current = remaining
                .Where(n => dfg.ContainsKey((current, n)))
                .OrderByDescending(n => dfg[(current, n)])
                .FirstOrDefault();

            if (current is null && remaining.Count > 0)
            {
                current = remaining
                    .OrderByDescending(a => outSum.GetValueOrDefault(a))
                    .First();
            }
        }

        ordered.AddRange(remaining.OrderByDescending(a => outSum.GetValueOrDefault(a)));

        var children = ordered.Select(ProcessTreeNode.Leaf);
        return ProcessTreeNode.Make(ProcessOperator.Fallback, children);
    }

    private static List<List<string>> FlattenToSequences(ProcessTreeNode node)
    {
        switch (node.Operator)
        {
            case ProcessOperator.Activity:
                return node.Activity is not null
                    ? [[node.Activity]]
                    : [[]];

            case ProcessOperator.Sequence:
            case ProcessOperator.Fallback:
            {
                IEnumerable<List<string>> acc = [[]];
                foreach (var child in node.Children)
                {
                    var childSeqs = FlattenToSequences(child);
                    acc = acc.SelectMany(prefix =>
                        childSeqs.Select(s => prefix.Concat(s).ToList()));
                }
                return acc.ToList();
            }

            case ProcessOperator.ExclusiveChoice:
            {
                var result = new List<List<string>>();
                foreach (var child in node.Children)
                    result.AddRange(FlattenToSequences(child));
                return result;
            }

            case ProcessOperator.Parallel:
            {
                var childGroups = node.Children
                    .Select(c => FlattenToSequences(c).Take(MaxRouteVariants).ToList())
                    .ToList();

                var combinations = CartesianProduct(childGroups);
                var result       = new List<List<string>>();

                foreach (var combo in combinations)
                {
                    foreach (var perm in GetPermutations(combo))
                    {
                        result.Add(perm.SelectMany(s => s).ToList());
                        if (result.Count >= MaxRouteVariants) return result;
                    }
                }
                return result;
            }

            case ProcessOperator.Loop:
            {
                if (node.Children.Count == 0) return [[]];

                var bodySeqs = FlattenToSequences(node.Children[0]);
                var redoSeqs = node.Children.Count > 1
                    ? FlattenToSequences(node.Children[1])
                    : [new List<string>()];

                var result = new List<List<string>>(bodySeqs);

                foreach (var body in bodySeqs)
                    foreach (var redo in redoSeqs)
                    {
                        result.Add([.. body, .. redo, .. body]);
                        if (result.Count >= MaxRouteVariants) return result;
                    }

                return result;
            }

            default:
                return [[]];
        }
    }

    private static List<List<string>> ExtractStagesFromTree(ProcessTreeNode tree)
    {
        var stages = new List<List<string>>();
        CollectStages(tree, stages);
        return stages.Count > 0 ? stages : [[]];
    }

    private static void CollectStages(ProcessTreeNode node, List<List<string>> stages)
    {
        switch (node.Operator)
        {
            case ProcessOperator.Activity:
                if (node.Activity is not null)
                    stages.Add([node.Activity]);
                break;

            case ProcessOperator.Sequence:
            case ProcessOperator.Fallback:
                foreach (var child in node.Children)
                    CollectStages(child, stages);
                break;

            case ProcessOperator.ExclusiveChoice:
                if (node.Children.Count > 0)
                    CollectStages(node.Children[0], stages);
                break;

            case ProcessOperator.Parallel:
                var leafs = CollectLeafActivities(node);
                if (leafs.Count > 0) stages.Add(leafs);
                break;

            case ProcessOperator.Loop:
                if (node.Children.Count > 0)
                    CollectStages(node.Children[0], stages);
                break;
        }
    }

    private static List<string> CollectLeafActivities(ProcessTreeNode node)
    {
        if (node.Operator == ProcessOperator.Activity && node.Activity is not null)
            return [node.Activity];
        return node.Children.SelectMany(CollectLeafActivities).ToList();
    }

    private static List<RouteVariant> BuildRouteVariants(
        List<List<string>> sequences,
        List<List<string>> traces,
        Dictionary<(string, string), decimal> transitionConf)
    {
        if (sequences.Count == 0) return [];

        var counts = new int[sequences.Count];

        foreach (var trace in traces)
        {
            int exactIdx = -1;
            for (int i = 0; i < sequences.Count; i++)
                if (SequencesEqual(trace, sequences[i]))
                {
                    exactIdx = i;
                    break;
                }

            if (exactIdx >= 0)
            {
                counts[exactIdx]++;
                continue;
            }

            int bestIdx   = 0;
            int bestScore = -1;
            for (int i = 0; i < sequences.Count; i++)
            {
                int score = CountSharedTransitions(trace, sequences[i]);
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }
            counts[bestIdx]++;
        }

        var indexed = sequences
            .Select((seq, i) => (seq, count: counts[i]))
            .OrderByDescending(x => x.count)
            .ToList();

        int totalTraces = traces.Count;
        var result      = new List<RouteVariant>();

        for (int vi = 0; vi < indexed.Count; vi++)
        {
            var (seq, cnt) = indexed[vi];
            var rareList   = FindRareTransitions(seq, transitionConf);

            result.Add(new RouteVariant
            {
                VariantNumber       = vi + 1,
                Operations          = seq,
                TraceCount          = cnt,
                TraceShare          = totalTraces > 0
                                      ? Math.Round(cnt * 100m / totalTraces, 1)
                                      : 0m,
                AverageConfidence   = CalcAvgConfidence(seq, transitionConf),
                HasRareTransitions  = rareList.Count > 0,
                RareTransitions     = rareList,
            });
        }

        return result;
    }

    private static Dictionary<(string, string), int> BuildDfg(List<List<string>> traces)
    {
        var dfg = new Dictionary<(string, string), int>();
        foreach (var trace in traces)
            for (int i = 0; i < trace.Count - 1; i++)
            {
                var key = (trace[i], trace[i + 1]);
                dfg[key] = dfg.GetValueOrDefault(key) + 1;
            }
        return dfg;
    }

    private static List<List<string>> FindSCCs(
        List<string> nodes,
        Dictionary<(string, string), int> edges)
    {
        var index   = new Dictionary<string, int>();
        var lowLink = new Dictionary<string, int>();
        var onStack = new Dictionary<string, bool>();
        var stack   = new Stack<string>();
        var sccs    = new List<List<string>>();
        int counter = 0;

        void StrongConnect(string v)
        {
            index[v] = lowLink[v] = counter++;
            stack.Push(v);
            onStack[v] = true;

            foreach (var w in nodes.Where(n => edges.ContainsKey((v, n))))
            {
                if (!index.ContainsKey(w))
                {
                    StrongConnect(w);
                    lowLink[v] = Math.Min(lowLink[v], lowLink[w]);
                }
                else if (onStack.GetValueOrDefault(w))
                {
                    lowLink[v] = Math.Min(lowLink[v], index[w]);
                }
            }

            if (lowLink[v] == index[v])
            {
                var scc = new List<string>();
                string w;
                do { w = stack.Pop(); onStack[w] = false; scc.Add(w); }
                while (w != v);
                sccs.Add(scc);
            }
        }

        foreach (var n in nodes)
            if (!index.ContainsKey(n))
                StrongConnect(n);

        return sccs;
    }

    private static List<List<string>> FindConnectedComponents(
        List<string> nodes,
        Dictionary<string, HashSet<string>> adj)
    {
        var visited    = new HashSet<string>();
        var components = new List<List<string>>();

        foreach (var start in nodes)
        {
            if (visited.Contains(start)) continue;
            var component = new List<string>();
            var queue     = new Queue<string>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!visited.Add(cur)) continue;
                component.Add(cur);
                foreach (var nb in adj.GetValueOrDefault(cur, []))
                    if (!visited.Contains(nb)) queue.Enqueue(nb);
            }
            components.Add(component);
        }
        return components;
    }

    private static List<List<string>> ProjectTraces(
        List<List<string>> traces,
        IEnumerable<string> activitySubset)
    {
        var actSet = new HashSet<string>(activitySubset);
        return traces
            .Select(t => NormalizeTrace(t.Where(a => actSet.Contains(a)).ToList()))
            .Where(t => t.Count > 0)
            .ToList();
    }

    private static List<List<string>> FilterTracesForGroup(
        List<List<string>> traces,
        IEnumerable<string> group)
    {
        var actSet = new HashSet<string>(group);
        return traces
            .Where(t => t.Any(a => actSet.Contains(a)))
            .Select(t => NormalizeTrace(t.Where(a => actSet.Contains(a)).ToList()))
            .Where(t => t.Count > 0)
            .ToList();
    }

    private static List<string> NormalizeTrace(List<string> trace)
    {
        if (trace.Count == 0) return trace;
        var result = new List<string> { trace[0] };
        for (int i = 1; i < trace.Count; i++)
            if (trace[i] != trace[i - 1])
                result.Add(trace[i]);
        return result;
    }

    private static bool ContainsOperator(ProcessTreeNode node, ProcessOperator op)
    {
        if (node.Operator == op) return true;
        return node.Children.Any(c => ContainsOperator(c, op));
    }

    private static decimal CalcAvgConfidence(
        List<string> sequence,
        Dictionary<(string, string), decimal> transitionConf)
    {
        if (sequence.Count < 2) return 100m;
        var confs = new List<decimal>();
        for (int i = 0; i < sequence.Count - 1; i++)
        {
            var key = (sequence[i], sequence[i + 1]);
            if (transitionConf.TryGetValue(key, out var c))
                confs.Add(c);
        }
        return confs.Count > 0 ? Math.Round(confs.Average(), 1) : 0m;
    }

    private static List<string> FindRareTransitions(
        List<string> sequence,
        Dictionary<(string, string), decimal> transitionConf)
    {
        var rare = new List<string>();
        for (int i = 0; i < sequence.Count - 1; i++)
        {
            var key = (sequence[i], sequence[i + 1]);
            if (transitionConf.TryGetValue(key, out var c) && c < RareTransitionThreshold)
                rare.Add($"{sequence[i]}→{sequence[i + 1]}");
        }
        return rare;
    }

    private static bool SequencesEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static int CountSharedTransitions(List<string> a, List<string> b)
    {
        var setA = new HashSet<(string, string)>();
        for (int i = 0; i < a.Count - 1; i++) setA.Add((a[i], a[i + 1]));
        int count = 0;
        for (int i = 0; i < b.Count - 1; i++)
            if (setA.Contains((b[i], b[i + 1]))) count++;
        return count;
    }

    private static IEnumerable<List<T>> CartesianProduct<T>(List<List<T>> groups)
    {
        IEnumerable<List<T>> result = [[]];
        foreach (var group in groups)
            result = result.SelectMany(prefix =>
                group.Select(item => prefix.Append(item).ToList()));
        return result;
    }

    private static IEnumerable<List<T>> GetPermutations<T>(List<T> items)
    {
        if (items.Count <= 1) { yield return items; yield break; }
        for (int i = 0; i < items.Count; i++)
        {
            var head = items[i];
            var rest = items.Where((_, idx) => idx != i).ToList();
            foreach (var perm in GetPermutations(rest))
                yield return [head, .. perm];
        }
    }

    private static IEnumerable<List<string>> CartesianPaths(List<List<string>> stages)
    {
        IEnumerable<List<string>> current = [[]];
        foreach (var stage in stages)
            current = current.SelectMany(p => stage.Select(a => p.Append(a).ToList()));
        return current;
    }

    private sealed class SequenceComparer : IEqualityComparer<List<string>>
    {
        public bool Equals(List<string>? x, List<string>? y) =>
            x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(List<string> obj) =>
            obj.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode()));
    }
}
