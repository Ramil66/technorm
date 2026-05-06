using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface ITechRouteService
{
    Task<List<TechRoute>> GetAllAsync();
    Task<TechRoute?> GetByIdAsync(int id);
    Task<TechRoute?> GetByIdWithStepsAsync(int id);
    Task<List<TechRoute>> GetByProductAsync(int productId);
    Task<TechRoute?> GetPublishedByProductAsync(int productId);
    Task<TechRoute> CreateAsync(TechRoute route);
    Task UpdateAsync(TechRoute route);
    Task PublishAsync(int id, int userId);
    Task ArchiveAsync(int id);
    Task<TechRoute> CreateNewVersionAsync(int sourceId, int userId);
    Task DeleteAsync(int id);
}

public class TechRouteService(IDbContextFactory<TechNormDbContext> factory) : ITechRouteService
{
    public async Task<List<TechRoute>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TechRoutes
            .Include(r => r.Product)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync();
    }

    public async Task<TechRoute?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TechRoutes
            .Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<TechRoute?> GetByIdWithStepsAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TechRoutes
            .Include(r => r.Product)
            .Include(r => r.Steps.OrderBy(s => s.SequenceNum))
                .ThenInclude(s => s.Operation)
            .Include(r => r.Steps)
                .ThenInclude(s => s.TimeNorms)
                    .ThenInclude(t => t.Resource)
            .Include(r => r.Steps)
                .ThenInclude(s => s.MaterialNorms)
                    .ThenInclude(mn => mn.Material)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<List<TechRoute>> GetByProductAsync(int productId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TechRoutes
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.Version)
            .ToListAsync();
    }

    public async Task<TechRoute?> GetPublishedByProductAsync(int productId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TechRoutes
            .Where(r => r.ProductId == productId && r.Status == "published")
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync();
    }

    public async Task<TechRoute> CreateAsync(TechRoute route)
    {
        route.CreatedAt = DateTime.UtcNow;
        route.UpdatedAt = DateTime.UtcNow;
        using var db = await factory.CreateDbContextAsync();
        db.TechRoutes.Add(route);
        await db.SaveChangesAsync();
        return route;
    }

    public async Task UpdateAsync(TechRoute route)
    {
        route.UpdatedAt = DateTime.UtcNow;
        using var db = await factory.CreateDbContextAsync();
        db.TechRoutes.Update(route);
        await db.SaveChangesAsync();
    }

    public async Task PublishAsync(int id, int userId)
    {
        using var db = await factory.CreateDbContextAsync();
        var route = await db.TechRoutes.FindAsync(id)
            ?? throw new InvalidOperationException($"TechRoute {id} не найден.");

        // Архивируем текущую опубликованную версию
        await db.TechRoutes
            .Where(r => r.ProductId == route.ProductId && r.Status == "published")
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, "archived"));

        await db.TechRoutes.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "published")
                .SetProperty(r => r.PublishedAt, DateTime.UtcNow)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
    }

    public async Task ArchiveAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.TechRoutes.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "archived")
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
    }

    public async Task<TechRoute> CreateNewVersionAsync(int sourceId, int userId)
    {
        using var db = await factory.CreateDbContextAsync();
        var source = await db.TechRoutes
            .Include(r => r.Steps)
                .ThenInclude(s => s.TimeNorms)
            .Include(r => r.Steps)
                .ThenInclude(s => s.MaterialNorms)
            .FirstOrDefaultAsync(r => r.Id == sourceId)
            ?? throw new InvalidOperationException($"TechRoute {sourceId} не найден.");

        var newRoute = new TechRoute
        {
            ProductId = source.ProductId,
            Version = source.Version + 1,
            Status = "draft",
            Name = source.Name,
            ProcessTree = source.ProcessTree,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.TechRoutes.Add(newRoute);
        await db.SaveChangesAsync();

        foreach (var step in source.Steps.OrderBy(s => s.SequenceNum))
        {
            var newStep = new RouteStep
            {
                RouteId = newRoute.Id,
                SequenceNum = step.SequenceNum,
                OperationId = step.OperationId,
                Description = step.Description,
                Parameters = step.Parameters,
            };
            db.RouteSteps.Add(newStep);
            await db.SaveChangesAsync();

            foreach (var tn in step.TimeNorms)
                db.TimeNorms.Add(new TimeNorm
                {
                    RouteStepId = newStep.Id,
                    ResourceId = tn.ResourceId,
                    NormValue = tn.NormValue,
                    IsManual = tn.IsManual,
                    UpdatedAt = DateTime.UtcNow,
                });

            foreach (var mn in step.MaterialNorms)
                db.MaterialNorms.Add(new MaterialNorm
                {
                    RouteStepId = newStep.Id,
                    MaterialId = mn.MaterialId,
                    ConsumptionRate = mn.ConsumptionRate,
                    UpdatedAt = DateTime.UtcNow,
                });
        }
        await db.SaveChangesAsync();
        return newRoute;
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.TechRoutes.Where(r => r.Id == id).ExecuteDeleteAsync();
    }
}
