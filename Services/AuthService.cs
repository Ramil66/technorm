using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace TechNormBlazor.Services;

public record LoginResult(bool Success, string? Error = null);

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string username, string password, HttpContext httpContext);
    Task LogoutAsync(HttpContext httpContext);
}

public class AuthService(IUserService userService) : IAuthService
{
    public async Task<LoginResult> LoginAsync(string username, string password, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new LoginResult(false, "Введите логин и пароль");

        var user = await userService.GetByUsernameAsync(username.Trim());

        if (user is null || !user.IsActive)
            return new LoginResult(false, "Неверный логин или пароль");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return new LoginResult(false, "Неверный логин или пароль");

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name,     user.Username),
            new("FullName",          user.FullName),
            new(ClaimTypes.Role,     user.Role),
            new("UserId",            user.Id.ToString()),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddHours(8),
            });

        await userService.UpdateLastLoginAsync(user.Id);
        return new LoginResult(true);
    }

    public async Task LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
