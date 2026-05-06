using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IRouteStepService
{
    Task<List<RouteStep>> GetByRouteAsync(int routeId);
    Task<RouteStep?> GetByIdAsync(int id);
    Task<RouteStep> CreateAsync(RouteStep step);
    Task UpdateAsync(RouteStep step);
    Task DeleteAsync(int id);
}

public class RouteStepService(IDbContextFactory<TechNormDbContext> factory) : IRouteStepService
{
    public async Task<List<RouteStep>> GetByRouteAsync(int routeId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.RouteSteps
            .Where(s => s.RouteId == routeId)
            .Include(s => s.Operation)
            .Include(s => s.TimeNorms).ThenInclude(t => t.Resource)
            .Include(s => s.MaterialNorms).ThenInclude(m => m.Material)
            .OrderBy(s => s.SequenceNum)
            .ToListAsync();
    }

    public async Task<RouteStep?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.RouteSteps
            .Include(s => s.Operation)
            .Include(s => s.TimeNorms).ThenInclude(t => t.Resource)
            .Include(s => s.MaterialNorms).ThenInclude(m => m.Material)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<RouteStep> CreateAsync(RouteStep step)
    {
        using var db = await factory.CreateDbContextAsync();
        db.RouteSteps.Add(step);
        await db.SaveChangesAsync();
        return step;
    }

    public async Task UpdateAsync(RouteStep step)
    {
        using var db = await factory.CreateDbContextAsync();
        db.RouteSteps.Update(step);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.RouteSteps.Where(s => s.Id == id).ExecuteDeleteAsync();
    }
}
