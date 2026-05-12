using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IProductService
{
    Task<List<Product>> GetAllAsync();
    Task<List<Product>> GetAllWithRoutesAsync();
    Task<Product?> GetByIdAsync(int id);
    Task<List<Product>> GetByTypeAsync(string type);
    Task<Product> CreateAsync(Product product);
    Task UpdateAsync(Product product);
    Task DeleteAsync(int id);
}

public class ProductService(IDbContextFactory<TechNormDbContext> factory) : IProductService
{
    public async Task<List<Product>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Products.OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<List<Product>> GetAllWithRoutesAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Products
            .Include(p => p.TechRoutes)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Products.FindAsync(id);
    }

    public async Task<List<Product>> GetByTypeAsync(string type)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Products
            .Where(p => p.Type == type)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Product> CreateAsync(Product product)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public async Task UpdateAsync(Product product)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Products.Update(product);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync();
    }
}
