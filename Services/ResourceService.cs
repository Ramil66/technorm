using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IResourceService
{
    Task<List<Resource>> GetAllAsync();
    Task<Resource?> GetByIdAsync(int id);
    Task<List<Resource>> GetActiveAsync();
    Task<Resource> CreateAsync(Resource resource);
    Task UpdateAsync(Resource resource);
    Task SetActiveAsync(int id, bool isActive);
}

public class ResourceService(IDbContextFactory<TechNormDbContext> factory) : IResourceService
{
    public async Task<List<Resource>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Resources.OrderBy(r => r.Name).ToListAsync();
    }

    public async Task<Resource?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Resources.FindAsync(id);
    }

    public async Task<List<Resource>> GetActiveAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Resources
            .Where(r => r.IsActive)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<Resource> CreateAsync(Resource resource)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Resources.Add(resource);
        await db.SaveChangesAsync();
        return resource;
    }

    public async Task UpdateAsync(Resource resource)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Resources.Update(resource);
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Resources.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, isActive));
    }
}
