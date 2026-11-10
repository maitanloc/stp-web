using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Stp.Api.Services;
using Stp.Core.Security;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Jobs;
using Stp.Infrastructure.Persistence;
using Stp.Infrastructure.Security;
using Stp.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddStpPersistence(builder.Configuration);
builder.Services.AddStpExcel(builder.Configuration);
builder.Services.AddStpStorage(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddStpJobs();
builder.Services.AddSingleton<TeamPresence>();
builder.Services.AddSingleton<TeamAudit>();

var developerAuthOptions = builder.Configuration
    .GetSection(DeveloperAuthOptions.SectionName)
    .Get<DeveloperAuthOptions>() ?? new DeveloperAuthOptions();

builder.Services.Configure<DeveloperAuthOptions>(
    builder.Configuration.GetSection(DeveloperAuthOptions.SectionName));

var developerAuthStatePath = DeveloperAuthStateStore.ResolveStatePath(
    developerAuthOptions,
    builder.Environment.ContentRootPath);

var developerSigningKey = new SymmetricSecurityKey(
    DeveloperAuthStateStore.ResolveSigningKey(developerAuthOptions, developerAuthStatePath));

builder.Services.AddSingleton<IDeveloperAuthService, DeveloperAuthService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = developerAuthOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = developerAuthOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = developerSigningKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 420L * 1024 * 1024;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 420L * 1024 * 1024;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Spa", policy => policy
        .WithOrigins(
            "http://localhost:5173",
            "http://localhost:4173",
            "http://localhost:3000")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

// Serve SPA build (copy của src/Stp.Web/dist) khi có wwwroot/index.html —
// cho phép chia sẻ app qua một cổng duy nhất (API + giao diện gốc /api mọi nơi).
// Không có wwwroot thì vẫn như cũ: API thuần, UI chạy qua dev server Vite.
var spaRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
var spaIndex = Path.Combine(spaRoot, "index.html");
if (File.Exists(spaIndex))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseCors("Spa");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/api/health", async (StpDbContext db, CancellationToken cancellationToken) =>
{
    var databaseOk = await db.Database.CanConnectAsync(cancellationToken);
    return Results.Ok(new
    {
        status = databaseOk ? "ok" : "degraded",
        database = databaseOk ? "connected" : "unavailable",
        databaseName = db.Database.GetDbConnection().Database,
        utc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/", () =>
{
    // Có SPA build thì trả giao diện; chưa có thì redirect health như cũ.
    var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
    if (File.Exists(spaIndex))
    {
        return Results.File(spaIndex, "text/html");
    }

    return Results.Redirect("/api/health");
});

// Deep link (/trich-yeu, /kho…) và refresh trang: trả index.html của SPA,
// nhưng /api/* không có route vẫn phải là 404 thật, không nuốt vào index.
app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
    if (!File.Exists(spaIndex))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    return context.Response.SendFileAsync(spaIndex);
});

app.Run();

public partial class Program;
