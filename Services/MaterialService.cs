using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IMaterialService
{
    Task<List<Material>> GetAllAsync();
    Task<Material?> GetByIdAsync(int id);
    Task<Material> CreateAsync(Material material);
    Task UpdateAsync(Material material);
    Task DeleteAsync(int id);
}

public class MaterialService(IDbContextFactory<TechNormDbContext> factory) : IMaterialService
{
    public async Task<List<Material>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Materials.OrderBy(m => m.Name).ToListAsync();
    }

    public async Task<Material?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Materials.FindAsync(id);
    }

    public async Task<Material> CreateAsync(Material material)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        return material;
    }

    public async Task UpdateAsync(Material material)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Materials.Update(material);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Materials.Where(m => m.Id == id).ExecuteDeleteAsync();
    }
}
