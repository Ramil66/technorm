using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IAuditLogService
{
    Task LogAsync(int? userId, string action, string? entityType = null, int? entityId = null,
        object? oldValue = null, object? newValue = null, string? ip = null, string? userAgent = null);
    Task<List<AuditLog>> GetRecentAsync(int count = 50);
    Task<List<AuditLog>> GetByUserAsync(int userId);
    Task<List<AuditLog>> GetByEntityAsync(string entityType, int entityId);
}

public class AuditLogService(IDbContextFactory<TechNormDbContext> factory) : IAuditLogService
{
    private static JsonDocument? ToJson(object? value) =>
        value is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(value));

    public async Task LogAsync(int? userId, string action, string? entityType = null, int? entityId = null,
        object? oldValue = null, object? newValue = null, string? ip = null, string? userAgent = null)
    {
        var entry = new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = ToJson(oldValue),
            NewValue = ToJson(newValue),
            IpAddress = ip,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow,
        };
        using var db = await factory.CreateDbContextAsync();
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditLog>> GetRecentAsync(int count = 50)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.AuditLogs
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetByUserAsync(int userId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.AuditLogs
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetByEntityAsync(string entityType, int entityId)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .Include(a => a.User)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }
}
