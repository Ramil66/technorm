using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface ICalculationHistoryService
{
    Task<List<CalculationHistory>> GetAllAsync();
    Task<List<CalculationHistory>> GetByProductAsync(int productId);
    Task<List<CalculationHistory>> GetByRouteAsync(int routeId);
    Task<CalculationHistory> RecordAsync(int? productId, int? routeId, int? calculatedBy, object metrics);
}

public class CalculationHistoryService(IDbContextFactory<TechNormDbContext> factory) : ICalculationHistoryService
{
    public async Task<List<CalculationHistory>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.CalculationHistories
            .Include(h => h.Product)
            .Include(h => h.Route)
            .Include(h => h.Calculator)
            .OrderByDescending(h => h.CalculatedAt)
            .ToListAsync();
    }

    public async Task<List<CalculationHistory>> GetByProductAsync(int productId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.CalculationHistories
            .Where(h => h.ProductId == productId)
            .Include(h => h.Route)
            .Include(h => h.Calculator)
            .OrderByDescending(h => h.CalculatedAt)
            .ToListAsync();
    }

    public async Task<List<CalculationHistory>> GetByRouteAsync(int routeId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.CalculationHistories
            .Where(h => h.RouteId == routeId)
            .Include(h => h.Product)
            .Include(h => h.Calculator)
            .OrderByDescending(h => h.CalculatedAt)
            .ToListAsync();
    }

    public async Task<CalculationHistory> RecordAsync(int? productId, int? routeId, int? calculatedBy, object metrics)
    {
        var metricsJson = JsonDocument.Parse(JsonSerializer.Serialize(metrics));
        var history = new CalculationHistory
        {
            ProductId = productId,
            RouteId = routeId,
            CalculatedBy = calculatedBy,
            CalculatedAt = DateTime.UtcNow,
            Metrics = metricsJson,
        };
        using var db = await factory.CreateDbContextAsync();
        db.CalculationHistories.Add(history);
        await db.SaveChangesAsync();
        return history;
    }
}
