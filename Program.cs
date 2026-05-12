using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TechNormBlazor.Components;
using TechNormBlazor.Components.Authentication;
using TechNormBlazor.Data;
using TechNormBlazor.Data.Models;
using TechNormBlazor.Data.Seeder;
using TechNormBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<TechNormDbContext>(options =>
    options.UseNpgsql(connectionString));
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

builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

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
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IDocumentParserService, DocumentParserService>();
builder.Services.AddScoped<IRouteBuilderService, RouteBuilderService>();

var app = builder.Build();

await SeedDatabase(app);

await SeedTestData(app);

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

app.MapGet("/api/files/{id:int}", async (int id, HttpContext ctx, IDocumentService docSvc) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    var doc = await docSvc.GetByIdAsync(id);
    if (doc is null) return Results.NotFound();

    Stream stream;
    try { stream = await docSvc.OpenFileAsync(id); }
    catch { return Results.NotFound(); }

    var ext = Path.GetExtension(doc.OriginalName).ToLowerInvariant();
    var mime = ext switch
    {
        ".pdf"          => "application/pdf",
        ".xlsx"         => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls"          => "application/vnd.ms-excel",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"          => "image/png",
        _               => "application/octet-stream",
    };

    var download = ctx.Request.Query.ContainsKey("download");
    var disposition = download ? "attachment" : "inline";
    ctx.Response.Headers["Content-Disposition"] = $"{disposition}; filename=\"{doc.OriginalName}\"";

    return Results.Stream(stream, mime, enableRangeProcessing: true);
}).RequireAuthorization();

app.MapGet("/api/files/{id:int}/preview", async (int id, HttpContext ctx, IDocumentService docSvc) =>
{
    if (ctx.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    var doc = await docSvc.GetByIdAsync(id);
    if (doc is null) return Results.NotFound();

    Stream stream;
    try { stream = await docSvc.OpenFileAsync(id); }
    catch { return Results.NotFound(); }

    using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
    var ws = workbook.Worksheets.First();

    var sb = new System.Text.StringBuilder();
    sb.Append("""
        <!DOCTYPE html><html><head><meta charset="utf-8">
        <style>
          body{font-family:Arial,sans-serif;font-size:12px;margin:0;padding:8px;background:#fff;}
          table{border-collapse:collapse;width:100%;}
          th{background:#1e3a5f;color:#fff;padding:5px 8px;text-align:left;white-space:nowrap;position:sticky;top:0;z-index:1;}
          td{padding:4px 8px;border-bottom:1px solid #e2e8f0;white-space:nowrap;}
          tr:nth-child(even) td{background:#f8fafc;}
          tr:hover td{background:#eff6ff;}
        </style></head><body><table>
        """);

    var lastCol = ws.LastCellUsed()?.Address.ColumnNumber ?? 1;
    var lastRow = ws.LastCellUsed()?.Address.RowNumber    ?? 1;

    for (int row = 1; row <= lastRow; row++)
    {
        sb.Append("<tr>");
        for (int col = 1; col <= lastCol; col++)
        {
            var cell = ws.Cell(row, col);
            var val  = System.Net.WebUtility.HtmlEncode(cell.GetString());
            if (row == 1)
                sb.Append($"<th>{val}</th>");
            else
                sb.Append($"<td>{val}</td>");
        }
        sb.Append("</tr>");
    }

    sb.Append("</table></body></html>");
    return Results.Content(sb.ToString(), "text/html; charset=utf-8");
}).RequireAuthorization();

app.Run();

static async Task SeedTestData(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db     = scope.ServiceProvider.GetRequiredService<TechNormDbContext>();
    var logger = app.Logger;

    await DbSeeder.SeedAsync(db, logger);

    var pdfPath = Path.Combine(app.Environment.ContentRootPath, "test-files", "events-sample.pdf");
    if (!File.Exists(pdfPath))
    {
        try
        {
            TestPdfGenerator.Generate(pdfPath);
            logger.LogInformation("Seed: тестовый PDF создан → {Path}", pdfPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Seed: не удалось создать тестовый PDF.");
        }
    }
}

static async Task SeedDatabase(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TechNormDbContext>();

    await db.Database.MigrateAsync();

    if (await db.Users.AnyAsync())
        return;

    var users = new[]
    {
        new User
        {
            Username     = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123", workFactor: 12),
            FullName     = "Администратор системы",
            Email        = "admin@technorm.local",
            Role         = "admin",
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow,
        },
        new User
        {
            Username     = "technolog",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("tech123", workFactor: 12),
            FullName     = "Технолог-нормировщик",
            Email        = "technolog@technorm.local",
            Role         = "technologist",
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow,
        },
    };

    db.Users.AddRange(users);
    await db.SaveChangesAsync();

    app.Logger.LogInformation("Seed: создано {Count} пользователей по умолчанию.", users.Length);
}
