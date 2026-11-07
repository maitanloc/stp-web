using Microsoft.EntityFrameworkCore;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Jobs;
using Stp.Infrastructure.Persistence;
using Stp.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddStpPersistence(builder.Configuration);
builder.Services.AddStpExcel(builder.Configuration);
builder.Services.AddStpStorage(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddStpJobs();

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
