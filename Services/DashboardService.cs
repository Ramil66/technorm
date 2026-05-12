using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public record DashboardStats(
    int TotalEvents,
    int TodayEvents,
    int ActiveRoutes,
    int ProductsInWork,
    int DocumentsProcessed);

public record UserProfileStats(
    int TotalEvents,
    int TotalDocuments,
    int RoutesViewed,
    int ActiveDays);

public interface IDashboardService
{
    Task<DashboardStats> GetStatsAsync();
    Task<int[]> GetWeeklyCountsAsync();
    Task<string[]> GetWeeklyLabelsAsync();
    Task<List<TechRoute>> GetRecentRoutesAsync(int count = 5);
    Task<List<EventLog>> GetRecentEventsAsync(int count = 10);
    Task<Dictionary<DateOnly, int>> GetUserActivityAsync(int userId);
    Task<List<AuditLog>> GetUserRecentActionsAsync(int userId, int count = 15);
    Task<UserProfileStats> GetUserStatsAsync(int userId);
}

public class DashboardService(IDbContextFactory<TechNormDbContext> factory) : IDashboardService
{
    public async Task<DashboardStats> GetStatsAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        var today = DateTime.UtcNow.Date;

        var totalEvents    = await db.EventLogs.CountAsync();
        var todayEvents    = await db.EventLogs.CountAsync(e => e.Timestamp.Date == today);
        var activeRoutes   = await db.TechRoutes.CountAsync(r => r.Status == "published");
        var productsInWork = await db.TechRoutes
            .Where(r => r.Status == "published")
            .Select(r => r.ProductId)
            .Distinct()
            .CountAsync();
        var docsProcessed  = await db.Documents.CountAsync(d => d.Status == "processed");

        return new DashboardStats(totalEvents, todayEvents, activeRoutes, productsInWork, docsProcessed);
    }

    public async Task<int[]> GetWeeklyCountsAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        var today  = DateTime.UtcNow.Date;
        var counts = new int[7];
        for (int i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            counts[6 - i] = await db.EventLogs.CountAsync(e => e.Timestamp.Date == day);
        }
        return counts;
    }

    public Task<string[]> GetWeeklyLabelsAsync()
    {
        var today  = DateTime.UtcNow.Date;
        var names  = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        var labels = new string[7];
        for (int i = 6; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            int dow = ((int)day.DayOfWeek + 6) % 7; // Mon=0 … Sun=6
            labels[6 - i] = names[dow];
        }
        return Task.FromResult(labels);
    }

    public async Task<List<TechRoute>> GetRecentRoutesAsync(int count = 5)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.TechRoutes
            .Include(r => r.Product)
            .OrderByDescending(r => r.UpdatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<EventLog>> GetRecentEventsAsync(int count = 10)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.EventLogs
            .Include(e => e.Product)
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToListAsync();
    }

    public async Task<Dictionary<DateOnly, int>> GetUserActivityAsync(int userId)
    {
        using var db = await factory.CreateDbContextAsync();
        var start = DateTime.UtcNow.Date.AddDays(-364);
        var rows = await db.EventLogs
            .Where(e => e.CreatedBy == userId && e.Timestamp >= start)
            .GroupBy(e => e.Timestamp.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(x => DateOnly.FromDateTime(x.Date), x => x.Count);
    }

    public async Task<List<AuditLog>> GetUserRecentActionsAsync(int userId, int count = 15)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.AuditLogs
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<UserProfileStats> GetUserStatsAsync(int userId)
    {
        using var db = await factory.CreateDbContextAsync();
        var totalEvents = await db.EventLogs.CountAsync(e => e.CreatedBy == userId);
        var totalDocs   = await db.Documents.CountAsync(d => d.UploadedBy == userId);
        var routesViewed = await db.AuditLogs
            .CountAsync(a => a.UserId == userId && a.EntityType == "TechRoute");
        var activeDays = await db.AuditLogs
            .Where(a => a.UserId == userId)
            .Select(a => a.CreatedAt.Date)
            .Distinct()
            .CountAsync();
        return new UserProfileStats(totalEvents, totalDocs, routesViewed, activeDays);
    }
}
