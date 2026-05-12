using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IMaterialNormService
{
    Task<List<MaterialNorm>> GetByRouteStepAsync(int routeStepId);
    Task<List<MaterialNorm>> GetAllWithDetailsAsync();
    Task<List<MaterialNorm>> GetByProductIdAsync(int productId);
    Task<MaterialNorm?> GetByIdAsync(int id);
    Task<MaterialNorm> UpsertAsync(MaterialNorm norm);
    Task DeleteAsync(int id);
}

public class MaterialNormService(IDbContextFactory<TechNormDbContext> factory) : IMaterialNormService
{
    public async Task<List<MaterialNorm>> GetByRouteStepAsync(int routeStepId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.MaterialNorms
            .Where(m => m.RouteStepId == routeStepId)
            .Include(m => m.Material)
            .ToListAsync();
    }

    public async Task<List<MaterialNorm>> GetAllWithDetailsAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.MaterialNorms
            .Include(m => m.Material)
            .Include(m => m.RouteStep)
                .ThenInclude(s => s.Operation)
            .Include(m => m.RouteStep)
                .ThenInclude(s => s.Route)
                    .ThenInclude(r => r.Product)
            .OrderBy(m => m.RouteStep.Route.Product.Name)
            .ThenBy(m => m.RouteStep.SequenceNum)
            .ToListAsync();
    }

    public async Task<List<MaterialNorm>> GetByProductIdAsync(int productId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.MaterialNorms
            .Include(m => m.Material)
            .Include(m => m.RouteStep)
                .ThenInclude(s => s.Operation)
            .Include(m => m.RouteStep)
                .ThenInclude(s => s.Route)
                    .ThenInclude(r => r.Product)
            .Where(m => m.RouteStep.Route.ProductId == productId)
            .OrderBy(m => m.RouteStep.SequenceNum)
            .ToListAsync();
    }

    public async Task<MaterialNorm?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.MaterialNorms
            .Include(m => m.Material)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<MaterialNorm> UpsertAsync(MaterialNorm norm)
    {
        norm.UpdatedAt = DateTime.UtcNow;
        using var db = await factory.CreateDbContextAsync();
        var existing = await db.MaterialNorms.FirstOrDefaultAsync(m =>
            m.RouteStepId == norm.RouteStepId && m.MaterialId == norm.MaterialId);
        if (existing is null)
        {
            db.MaterialNorms.Add(norm);
        }
        else
        {
            existing.ConsumptionRate = norm.ConsumptionRate;
            existing.UpdatedAt = norm.UpdatedAt;
        }
        await db.SaveChangesAsync();
        return norm;
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.MaterialNorms.Where(m => m.Id == id).ExecuteDeleteAsync();
    }
}
