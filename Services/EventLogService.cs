using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IEventLogService
{
    Task<List<EventLog>> GetAllAsync();
    Task<List<EventLog>> GetByCaseAsync(string caseId);
    Task<List<EventLog>> GetByProductAsync(int productId);
    Task<EventLog> CreateAsync(EventLog eventLog);
    Task<List<EventLog>> CreateBatchAsync(IEnumerable<EventLog> events);
    Task DeleteAsync(long id);
}

public class EventLogService(IDbContextFactory<TechNormDbContext> factory) : IEventLogService
{
    public async Task<List<EventLog>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.EventLogs
            .Include(e => e.Product)
            .Include(e => e.Resource)
            .Include(e => e.SourceDocument)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    public async Task<List<EventLog>> GetByCaseAsync(string caseId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.EventLogs
            .Where(e => e.CaseId == caseId)
            .Include(e => e.Resource)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();
    }

    public async Task<List<EventLog>> GetByProductAsync(int productId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.EventLogs
            .Where(e => e.ProductId == productId)
            .Include(e => e.Resource)
            .Include(e => e.SourceDocument)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    public async Task<EventLog> CreateAsync(EventLog eventLog)
    {
        eventLog.CreatedAt = DateTime.UtcNow;
        using var db = await factory.CreateDbContextAsync();
        db.EventLogs.Add(eventLog);
        await db.SaveChangesAsync();
        return eventLog;
    }

    public async Task<List<EventLog>> CreateBatchAsync(IEnumerable<EventLog> events)
    {
        var list = events.ToList();
        var now = DateTime.UtcNow;
        foreach (var e in list)
            e.CreatedAt = now;
        using var db = await factory.CreateDbContextAsync();
        db.EventLogs.AddRange(list);
        await db.SaveChangesAsync();
        return list;
    }

    public async Task DeleteAsync(long id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.EventLogs.Where(e => e.Id == id).ExecuteDeleteAsync();
    }
}
