using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface INsiUpdateService
{
    bool IsAutoUpdateEnabled { get; }
    void SetAutoUpdate(bool enabled);
    Task UpdateRouteNormsAsync(int routeId, int? userId = null);
    Task UpdateAllPublishedRoutesAsync(int? userId = null);
}

public class NsiUpdateService(IDbContextFactory<TechNormDbContext> factory) : INsiUpdateService
{
    private bool _autoUpdateEnabled = true;

    public bool IsAutoUpdateEnabled => _autoUpdateEnabled;

    public void SetAutoUpdate(bool enabled) => _autoUpdateEnabled = enabled;

    public async Task UpdateRouteNormsAsync(int routeId, int? userId = null)
    {
        using var db = await factory.CreateDbContextAsync();

        var route = await db.TechRoutes
            .Include(r => r.Steps)
                .ThenInclude(s => s.Operation)
            .FirstOrDefaultAsync(r => r.Id == routeId);

        if (route is null) return;

        var updatedSteps = new List<object>();

        foreach (var step in route.Steps.Where(s => s.Operation is not null))
        {
            var activity = step.Operation!.Name;

            var avgTicks = await db.EventLogs
                .Where(e => e.Activity == activity
                         && e.ProductId == route.ProductId
                         && e.Duration != null)
                .AverageAsync(e => (double?)e.Duration!.Value.Ticks);

            if (avgTicks is null) continue;

            var avgMinutes = (decimal)Math.Round(
                TimeSpan.FromTicks((long)avgTicks.Value).TotalMinutes, 2);

            var existing = await db.TimeNorms.FirstOrDefaultAsync(t =>
                t.RouteStepId == step.Id && t.ResourceId == null);

            if (existing is null)
            {
                db.TimeNorms.Add(new TimeNorm
                {
                    RouteStepId = step.Id,
                    ResourceId = null,
                    NormValue = avgMinutes,
                    IsManual = false,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                existing.NormValue = avgMinutes;
                existing.IsManual = false;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            updatedSteps.Add(new
            {
                StepId = step.Id,
                Activity = activity,
                NormMinutes = avgMinutes,
            });
        }

        await db.TechRoutes.Where(r => r.Id == routeId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UpdatedAt, DateTime.UtcNow));

        if (updatedSteps.Count > 0)
        {
            var metricsJson = System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    UpdatedAt = DateTime.UtcNow,
                    StepsCount = updatedSteps.Count,
                    Steps = updatedSteps,
                }));
            db.CalculationHistories.Add(new CalculationHistory
            {
                ProductId = route.ProductId,
                RouteId = routeId,
                CalculatedBy = userId,
                CalculatedAt = DateTime.UtcNow,
                Metrics = metricsJson,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task UpdateAllPublishedRoutesAsync(int? userId = null)
    {
        using var db = await factory.CreateDbContextAsync();
        var publishedIds = await db.TechRoutes
            .Where(r => r.Status == "published")
            .Select(r => r.Id)
            .ToListAsync();

        foreach (var id in publishedIds)
            await UpdateRouteNormsAsync(id, userId);
    }
}
