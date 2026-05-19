using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

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
    public int CaseCount { get; set; }
    public decimal Confidence { get; set; }
    public decimal AvgDurationMin { get; set; }
    public decimal MinDurationMin { get; set; }
    public decimal MaxDurationMin { get; set; }
    public List<string> Resources { get; set; } = [];
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
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalCases { get; set; }
    public int TotalEvents { get; set; }
    public List<MinerActivityInfo>   Activities          { get; set; } = [];
    public List<List<string>>        Stages              { get; set; } = [];
    public decimal                   OverallConfidence   { get; set; }
    public decimal                   Coverage            { get; set; }
    public int                       RareTransitionCount { get; set; }
    public List<MinerTransitionInfo> Transitions         { get; set; } = [];
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
    IOperationMatcherService            matcherSvc) : IRouteBuilderService
{
    private const decimal RareTransitionThreshold = 10m;

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

        // Coverage: отбираем только события с непустой активностью
        var validEvents = events.Where(e => !string.IsNullOrWhiteSpace(e.Activity)).ToList();
        var coverage    = Math.Round(validEvents.Count * 100m / events.Count, 1);

        if (validEvents.Count == 0)
            return new MinerResult
            {
                Success      = false,
                ErrorMessage = "Нет событий с заполненной активностью для построения маршрута",
            };

        var traces = validEvents
            .GroupBy(e => e.CaseId)
            .Select(g => g.OrderBy(e => e.Timestamp).ToList())
            .ToList();

        int totalCases = traces.Count;

        var actCases       = new Dictionary<string, HashSet<string>>();
        var actDurations   = new Dictionary<string, List<decimal>>();
        var actResources   = new Dictionary<string, HashSet<string>>();
        var actResDurs     = new Dictionary<(string Act, string Res), List<decimal>>();

        foreach (var trace in traces)
        {
            var seenDuration = new HashSet<string>();
            foreach (var evt in trace)
            {
                var act = evt.Activity;

                if (!actCases.ContainsKey(act))     actCases[act]     = [];
                if (!actResources.ContainsKey(act)) actResources[act] = [];

                actCases[act].Add(evt.CaseId);

                if (evt.Resource is not null)
                    actResources[act].Add(evt.Resource.Name);

                if (evt.Duration.HasValue)
                {
                    var mins = (decimal)evt.Duration.Value.TotalMinutes;

                    if (seenDuration.Add(act))
                    {
                        if (!actDurations.ContainsKey(act)) actDurations[act] = [];
                        actDurations[act].Add(mins);
                    }

                    if (evt.Resource is not null)
                    {
                        var key = (act, evt.Resource.Name);
                        if (!actResDurs.ContainsKey(key)) actResDurs[key] = [];
                        actResDurs[key].Add(mins);
                    }
                }
            }
        }

        var allActs = actCases.Keys.ToList();

        var dfg      = new Dictionary<(string From, string To), int>();
        var startCnt = new Dictionary<string, int>();
        var endCnt   = new Dictionary<string, int>();

        foreach (var trace in traces)
        {
            if (trace.Count == 0) continue;
            var seq = trace.Select(e => e.Activity).ToList();

            startCnt[seq[0]]  = startCnt.GetValueOrDefault(seq[0])  + 1;
            endCnt[seq[^1]]   = endCnt.GetValueOrDefault(seq[^1])   + 1;

            for (int i = 0; i < seq.Count - 1; i++)
            {
                if (seq[i] == seq[i + 1]) continue;
                var key = (seq[i], seq[i + 1]);
                dfg[key] = dfg.GetValueOrDefault(key) + 1;
            }
        }

        // Transition Confidence: C(A→B) = Count(A→B) / Σ Count(A→X) * 100
        var outTotals = dfg
            .GroupBy(kv => kv.Key.From)
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));

        var transitions = dfg
            .Select(kv =>
            {
                var outTotal = outTotals.GetValueOrDefault(kv.Key.From, 0);
                var conf     = outTotal > 0 ? Math.Round(kv.Value * 100m / outTotal, 1) : 0m;
                // Rare: переход встречается менее чем в RareTransitionThreshold% исходящих из From
                return new MinerTransitionInfo
                {
                    From       = kv.Key.From,
                    To         = kv.Key.To,
                    Count      = kv.Value,
                    Confidence = conf,
                    IsRare     = conf < RareTransitionThreshold,
                };
            })
            .OrderBy(t => t.From)
            .ThenByDescending(t => t.Count)
            .ThenBy(t => t.To)
            .ToList();

        var rareTransitionCount = transitions.Count(t => t.IsRare);

        var ordered = InductiveMinerOrder(allActs, dfg, startCnt, endCnt);
        var stages  = DetectParallelStages(ordered, dfg);

        var actOrder = stages.SelectMany(s => s).ToList();

        var actInfos = actOrder.Select(act =>
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

        return new MinerResult
        {
            Success             = true,
            TotalCases          = totalCases,
            TotalEvents         = events.Count,
            Activities          = actInfos,
            Stages              = stages,
            OverallConfidence   = overallConf,
            Coverage            = coverage,
            RareTransitionCount = rareTransitionCount,
            Transitions         = transitions,
        };
    }

    public async Task<List<TechRoute>> BuildRouteFromMinerAsync(
        MinerResult result, int productId, string routeName, int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var resources  = await db.Resources.ToListAsync();
        var actLookup  = result.Activities.ToDictionary(a => a.Name);

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
                {
                    if (act.ResourceStats.Any())
                    {
                        foreach (var rs in act.ResourceStats)
                        {
                            var resource = resources.FirstOrDefault(r =>
                                string.Equals(r.Name, rs.ResourceName,
                                              StringComparison.OrdinalIgnoreCase));
                            if (resource is not null && rs.AvgDurationMin > 0)
                                await timeNormSvc.UpsertAsync(new TimeNorm
                                {
                                    RouteStepId = step.Id, ResourceId = resource.Id,
                                    NormValue = rs.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
                                });
                        }
                    }
                    else if (act.AvgDurationMin > 0)
                    {
                        await timeNormSvc.UpsertAsync(new TimeNorm
                        {
                            RouteStepId = step.Id, ResourceId = null,
                            NormValue = act.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
                        });
                    }
                }
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
            r.Status == "published" &&
            r.IsAutoUpdate &&
            r.SourceEventCount != null);
        if (route is null) return;

        var result = await MineFromEventsAsync(productId);
        if (!result.Success) return;

        var stepIds = await db.RouteSteps
            .Where(s => s.RouteId == route.Id)
            .Select(s => s.Id)
            .ToListAsync();

        await db.TimeNorms.Where(t => stepIds.Contains(t.RouteStepId)).ExecuteDeleteAsync();
        await db.MaterialNorms.Where(m => stepIds.Contains(m.RouteStepId)).ExecuteDeleteAsync();
        await db.RouteSteps.Where(s => s.RouteId == route.Id).ExecuteDeleteAsync();

        var resources  = await db.Resources.ToListAsync();
        var actLookup  = result.Activities.ToDictionary(a => a.Name);

        int seqNum = 0;
        foreach (var stage in result.Stages)
        {
            seqNum++;
            var actName = stage[0]; // flatten parallel — take first activity per stage
            var act     = actLookup.GetValueOrDefault(actName);
            var match   = await matcherSvc.MatchAsync(actName);
            var op      = match.Status != MatchStatus.Unmatched ? match.Operation : null;

            var step = await stepSvc.CreateAsync(new RouteStep
            {
                RouteId     = route.Id,
                SequenceNum = seqNum,
                OperationId = op?.Id,
                Description = op is null ? actName : null,
            });

            if (act is not null)
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
                                RouteStepId = step.Id, ResourceId = resource.Id,
                                NormValue = rs.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
                            });
                    }
                }
                else if (act.AvgDurationMin > 0)
                {
                    await timeNormSvc.UpsertAsync(new TimeNorm
                    {
                        RouteStepId = step.Id, ResourceId = null,
                        NormValue = act.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
                    });
                }
            }
        }

        await db.TechRoutes.Where(r => r.Id == route.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SourceEventCount, result.TotalEvents)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
    }

    public async Task ManualRebuildAsync(int routeId, int userId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var route = await db.TechRoutes.FindAsync(routeId);
        if (route is null || !route.SourceEventCount.HasValue) return;

        var result = await MineFromEventsAsync(route.ProductId);
        if (!result.Success) return;

        var stepIds = await db.RouteSteps
            .Where(s => s.RouteId == routeId)
            .Select(s => s.Id)
            .ToListAsync();

        await db.TimeNorms.Where(t => stepIds.Contains(t.RouteStepId)).ExecuteDeleteAsync();
        await db.MaterialNorms.Where(m => stepIds.Contains(m.RouteStepId)).ExecuteDeleteAsync();
        await db.RouteSteps.Where(s => s.RouteId == routeId).ExecuteDeleteAsync();

        var resources  = await db.Resources.ToListAsync();
        var actLookup  = result.Activities.ToDictionary(a => a.Name);

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
                                RouteStepId = step.Id, ResourceId = resource.Id,
                                NormValue = rs.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
                            });
                    }
                }
                else if (act.AvgDurationMin > 0)
                {
                    await timeNormSvc.UpsertAsync(new TimeNorm
                    {
                        RouteStepId = step.Id, ResourceId = null,
                        NormValue = act.AvgDurationMin, IsManual = false, UpdatedAt = DateTime.UtcNow,
                    });
                }
            }
        }

        await db.TechRoutes.Where(r => r.Id == routeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SourceEventCount, result.TotalEvents)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
    }

    private static IEnumerable<List<string>> CartesianPaths(List<List<string>> stages)
    {
        IEnumerable<List<string>> current = [[]];
        foreach (var stage in stages)
            current = current.SelectMany(p => stage.Select(a => p.Append(a).ToList()));
        return current;
    }

    private static List<string> InductiveMinerOrder(
        List<string>                       acts,
        Dictionary<(string, string), int>  dfg,
        Dictionary<string, int>            startCnt,
        Dictionary<string, int>            endCnt)
    {
        if (acts.Count <= 1) return acts;

        var inDeg    = acts.ToDictionary(a => a, _ => 0);
        var outEdges = acts.ToDictionary(a => a, _ => new List<(string To, int Freq)>());

        foreach (var ((from, to), freq) in dfg)
        {
            if (!inDeg.ContainsKey(from) || !inDeg.ContainsKey(to)) continue;
            inDeg[to]++;
            outEdges[from].Add((to, freq));
        }

        var queue   = new PriorityQueue<string, int>();
        var visited = new HashSet<string>();
        var sorted  = new List<string>();

        foreach (var a in acts.Where(a => inDeg[a] == 0))
            queue.Enqueue(a, -(startCnt.GetValueOrDefault(a)));

        if (queue.Count == 0)
        {
            var seed = acts.OrderByDescending(a => startCnt.GetValueOrDefault(a)).First();
            queue.Enqueue(seed, 0);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node)) continue;
            sorted.Add(node);

            foreach (var (next, weight) in outEdges[node]
                                              .OrderByDescending(x => x.Freq))
            {
                if (visited.Contains(next)) continue;
                inDeg[next]--;
                if (inDeg[next] == 0)
                    queue.Enqueue(next, -(startCnt.GetValueOrDefault(next)));
            }
        }

        foreach (var act in acts.Except(visited)
                            .OrderByDescending(a => startCnt.GetValueOrDefault(a)))
            sorted.Add(act);

        return sorted;
    }

    private static List<List<string>> DetectParallelStages(
        List<string>                      ordered,
        Dictionary<(string, string), int> dfg)
    {
        var stages = new List<List<string>>();
        if (ordered.Count == 0) return stages;

        var currentStage = new List<string> { ordered[0] };

        for (int i = 1; i < ordered.Count; i++)
        {
            var curr = ordered[i];

            bool isParallel = currentStage.Count > 0 && currentStage.All(prev =>
                dfg.ContainsKey((prev, curr)) && dfg.ContainsKey((curr, prev)));

            if (isParallel)
                currentStage.Add(curr);
            else
            {
                stages.Add([.. currentStage]);
                currentStage = [curr];
            }
        }
        stages.Add([.. currentStage]);

        return stages;
    }
}
