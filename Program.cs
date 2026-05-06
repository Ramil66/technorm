using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Components;
using TechNormBlazor.Components.Authentication;
using TechNormBlazor.Data;
using TechNormBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ====================== БД ======================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<TechNormDbContext>(options =>
    options.UseNpgsql(connectionString));
// =================================================

// ====================== Авторизация ==============
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/login";
        options.LogoutPath       = "/logout";
        options.Cookie.Name      = "TechNorm.Auth";
        options.Cookie.HttpOnly  = true;
        options.Cookie.SameSite  = SameSiteMode.Strict;
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Кастомный провайдер: переодически проверяет активность пользователя в БД
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
// =================================================

// ====================== Сервисы НСИ ==============
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IOperationService, OperationService>();
builder.Services.AddScoped<IResourceService, ResourceService>();
builder.Services.AddScoped<IMaterialService, MaterialService>();
builder.Services.AddScoped<ITechRouteService, TechRouteService>();
builder.Services.AddScoped<IRouteStepService, RouteStepService>();
builder.Services.AddScoped<ITimeNormService, TimeNormService>();
builder.Services.AddScoped<IMaterialNormService, MaterialNormService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IEventLogService, EventLogService>();
builder.Services.AddScoped<ICalculationHistoryService, CalculationHistoryService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddSingleton<INsiUpdateService, NsiUpdateService>();
// =================================================

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
