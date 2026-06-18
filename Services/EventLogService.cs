using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IEventLogService
{
    Task<List<EventLog>> GetAllAsync();
    Task<(List<EventLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? productId, string? source, string? search);
    Task<List<EventLog>> GetByCaseAsync(string caseId);
    Task<List<EventLog>> GetByProductAsync(int productId);
    Task<EventLog> CreateAsync(EventLog eventLog);
    Task<List<EventLog>> CreateBatchAsync(IEnumerable<EventLog> events);
    Task UpdateAsync(EventLog eventLog);
    Task DeleteAsync(long id);
}

public class EventLogService(
    IDbContextFactory<TechNormDbContext> factory,
    IConformanceCheckingService          conformanceSvc,
    ILogger<EventLogService>             logger) : IEventLogService
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

    public async Task<(List<EventLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? productId, string? source, string? search)
    {
        using var db = await factory.CreateDbContextAsync();
        var q = db.EventLogs.AsQueryable();

        if (productId.HasValue && productId.Value > 0)
            q = q.Where(e => e.ProductId == productId.Value);
        if (!string.IsNullOrEmpty(source))
            q = q.Where(e => e.Source == source);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(e => e.Activity.Contains(search));

        var total = await q.CountAsync();
        var items = await q
            .Include(e => e.Product)
            .Include(e => e.Resource)
            .Include(e => e.SourceDocument)
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<List<EventLog>> GetByCaseAsync(string caseId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.EventLogs
            .Where(e => e.CaseId == caseId)
            .Include(e => e.Resource)
            .Include(e => e.Product)
            .Include(e => e.SourceDocument)
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

        if (eventLog.ProductId.HasValue)
            await TriggerPciAsync(eventLog.ProductId.Value);

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

        foreach (var pid in list.Where(e => e.ProductId.HasValue)
                                .Select(e => e.ProductId!.Value)
                                .Distinct())
            await TriggerPciAsync(pid);

        return list;
    }

    private async Task TriggerPciAsync(int productId)
    {
        try
        {
            await conformanceSvc.TriggerRecalculationAsync(productId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "PCI recalculation failed after event creation for product {ProductId}", productId);
        }
    }

    public async Task UpdateAsync(EventLog eventLog)
    {
        using var db = await factory.CreateDbContextAsync();
        var existing = await db.EventLogs.FindAsync(eventLog.Id);
        if (existing is null) return;
        existing.CaseId     = eventLog.CaseId;
        existing.Activity   = eventLog.Activity;
        existing.Timestamp  = eventLog.Timestamp;
        existing.ProductId  = eventLog.ProductId;
        existing.ResourceId = eventLog.ResourceId;
        existing.Duration   = eventLog.Duration;
        existing.Metadata   = eventLog.Metadata;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(long id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.EventLogs.Where(e => e.Id == id).ExecuteDeleteAsync();
    }
}
