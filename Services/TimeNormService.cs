using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface ITimeNormService
{
    Task<List<TimeNorm>> GetByRouteStepAsync(int routeStepId);
    Task<TimeNorm?> GetByIdAsync(int id);
    Task<TimeNorm> UpsertAsync(TimeNorm norm);
    Task DeleteAsync(int id);
    Task RecalculateFromEventsAsync(int routeStepId, string activity);
}

public class TimeNormService(IDbContextFactory<TechNormDbContext> factory) : ITimeNormService
{
    public async Task<List<TimeNorm>> GetByRouteStepAsync(int routeStepId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TimeNorms
            .Where(t => t.RouteStepId == routeStepId)
            .Include(t => t.Resource)
            .ToListAsync();
    }

    public async Task<TimeNorm?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TimeNorms.Include(t => t.Resource).FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<TimeNorm> UpsertAsync(TimeNorm norm)
    {
        norm.UpdatedAt = DateTime.UtcNow;
        using var db = await factory.CreateDbContextAsync();
        var existing = await db.TimeNorms.FirstOrDefaultAsync(t =>
            t.RouteStepId == norm.RouteStepId && t.ResourceId == norm.ResourceId);
        if (existing is null)
        {
            db.TimeNorms.Add(norm);
        }
        else
        {
            existing.NormValue = norm.NormValue;
            existing.IsManual = norm.IsManual;
            existing.UpdatedAt = norm.UpdatedAt;
        }
        await db.SaveChangesAsync();
        return norm;
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.TimeNorms.Where(t => t.Id == id).ExecuteDeleteAsync();
    }

    public async Task RecalculateFromEventsAsync(int routeStepId, string activity)
    {
        using var db = await factory.CreateDbContextAsync();
        var step = await db.RouteSteps.FindAsync(routeStepId);
        if (step is null) return;
        var route = await db.TechRoutes.FindAsync(step.RouteId);
        if (route is null) return;

        var avgTicks = await db.EventLogs
            .Where(e => e.Activity == activity
                     && e.ProductId == route.ProductId
                     && e.Duration != null)
            .AverageAsync(e => (double?)e.Duration!.Value.Ticks);

        if (avgTicks is null) return;

        var avgMinutes = (decimal)Math.Round(
            TimeSpan.FromTicks((long)avgTicks.Value).TotalMinutes, 2);

        await UpsertAsync(new TimeNorm
        {
            RouteStepId = routeStepId,
            ResourceId = null,
            NormValue = avgMinutes,
            IsManual = false,
        });
    }
}
