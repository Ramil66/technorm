using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;

namespace TechNormBlazor.Services;

public interface IUserService
{
    Task<List<User>> GetAllAsync();
    Task<User?> GetByIdAsync(int id);
    Task<User?> GetByUsernameAsync(string username);
    Task<User> CreateAsync(User user, string plainPassword);
    Task UpdateAsync(User user);
    Task ChangePasswordAsync(int id, string newPlainPassword);
    Task SetActiveAsync(int id, bool isActive);
    Task DeleteAsync(int id);
    Task<bool> ValidateCredentialsAsync(string username, string plainPassword);
    Task UpdateLastLoginAsync(int id);
}

public class UserService(IDbContextFactory<TechNormDbContext> factory) : IUserService
{
    private static string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public async Task<List<User>> GetAllAsync()
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Users.OrderBy(u => u.FullName).ToListAsync();
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Users.FindAsync(id);
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var db = await factory.CreateDbContextAsync();
        return await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User> CreateAsync(User user, string plainPassword)
    {
        user.PasswordHash = HashPassword(plainPassword);
        using var db = await factory.CreateDbContextAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        using var db = await factory.CreateDbContextAsync();
        db.Users.Update(user);
        await db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(int id, string newPlainPassword)
    {
        var hash = HashPassword(newPlainPassword);
        using var db = await factory.CreateDbContextAsync();
        await db.Users.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, hash));
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Users.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, isActive));
    }

    public async Task DeleteAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Documents.Where(d => d.UploadedBy == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.UploadedBy, (int?)null));
        await db.TechRoutes.Where(r => r.CreatedBy == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedBy, (int?)null));
        await db.AuditLogs.Where(a => a.UserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, (int?)null));
        await db.EventLogs.Where(e => e.CreatedBy == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedBy, (int?)null));
        await db.CalculationHistories.Where(c => c.CalculatedBy == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CalculatedBy, (int?)null));
        await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync();
    }

    public async Task<bool> ValidateCredentialsAsync(string username, string plainPassword)
    {
        using var db = await factory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user is null) return false;
        return BCrypt.Net.BCrypt.Verify(plainPassword, user.PasswordHash);
    }

    public async Task UpdateLastLoginAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Users.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLogin, DateTime.UtcNow));
    }
}
