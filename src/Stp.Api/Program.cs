using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

app.MapGet("/", () => Results.Redirect("/api/health"));

app.Run();

public partial class Program;
