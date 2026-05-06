using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using TechNormBlazor.Services;

namespace TechNormBlazor.Components.Authentication;

public class CustomAuthStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken)
    {
        var username = authenticationState.User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return false;

        await using var scope = scopeFactory.CreateAsyncScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var user = await userService.GetByUsernameAsync(username);
        return user is { IsActive: true };
    }
}
