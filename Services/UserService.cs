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
    Task SetActiveAsync(int id, bool isActive);
    Task<bool> ValidateCredentialsAsync(string username, string plainPassword);
    Task UpdateLastLoginAsync(int id);
}

public class UserService(IDbContextFactory<TechNormDbContext> factory) : IUserService
{
    private static string HashPassword(string password) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(password)));

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

    public async Task SetActiveAsync(int id, bool isActive)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Users.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, isActive));
    }

    public async Task<bool> ValidateCredentialsAsync(string username, string plainPassword)
    {
        using var db = await factory.CreateDbContextAsync();
        var hash = HashPassword(plainPassword);
        return await db.Users.AnyAsync(u =>
            u.Username == username && u.PasswordHash == hash && u.IsActive);
    }

    public async Task UpdateLastLoginAsync(int id)
    {
        using var db = await factory.CreateDbContextAsync();
        await db.Users.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLogin, DateTime.UtcNow));
    }
}
