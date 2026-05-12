using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IOperationService
{
    Task<List<Operation>> GetAllAsync();
    Task<Operation?> GetByIdAsync(int id);
    Task<Operation> CreateAsync(Operation operation);
    Task UpdateAsync(Operation operation);
    Task DeleteAsync(int id);
}

public class OperationService(IDbContextFactory<TechNormDbContext> factory) : IOperationService
{
    public async Task<List<Operation>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Operations.OrderBy(o => o.Name).ToListAsync();
    }

    public async Task<Operation?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Operations.FindAsync(id);
    }

    public async Task<Operation> CreateAsync(Operation operation)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Operations.Add(operation);
        await db.SaveChangesAsync();
        return operation;
    }

    public async Task UpdateAsync(Operation operation)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Operations.Update(operation);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        // Nullify FK in route_steps (operation_id is nullable)
        await db.RouteSteps.Where(s => s.OperationId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.OperationId, (int?)null));
        await db.Operations.Where(o => o.Id == id).ExecuteDeleteAsync();
    }
}
