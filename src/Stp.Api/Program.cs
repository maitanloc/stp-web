using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Stp.Api.Services;
using Stp.Core.Domain;
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
builder.Services.AddSingleton<NotificationService>();
// F1: realtime presence/hành động nhóm — poll REST vẫn chạy song song dự phòng.
builder.Services.AddSignalR();

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
    })
    // Cookie phiên cho tài khoản nhóm nội bộ (scheme "StpTeam") — song song
    // với JWT developer, hai hệ không đụng nhau.
    .AddCookie(StpTeamIdentity.SchemeName, options =>
    {
        options.Cookie.Name = ".stp.team";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        // Endpoint API: trả 401/403 JSON thẳng, không redirect trang đăng nhập.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// DataProtection: cookie phiên "StpTeam" được mã hoá bằng key DataProtection.
// Mặc định key sinh trong bộ nhớ — restart service là key mới, TOÀN BỘ cookie
// thành viên chết và cả phòng rơi về trang đăng nhập dù hứa "phiên 24 giờ".
// Persist key xuống đĩa + tên ứng dụng cố định → restart thoải mái, cookie sống.
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "data", "runtime", "keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("StpArchive");

// Khoá bắt buộc đăng nhập nhóm — chỉ bật sau khi đã tạo tài khoản cho từng
// người (appsettings "StpTeam:RequireLogin"). Còn tắt thì ai không có tài
// khoản vẫn dùng app như cũ (tự khai tên), không gián đoạn người đang nhập liệu.
var requireTeamLogin = builder.Configuration.GetValue<bool>("StpTeam:RequireLogin");

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
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            var name = ctx.File.Name;
            if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            {
                // index.html PHẢI revalidate mỗi lần load: các lần deploy SPA chỉ
                // đổi tên file hash, nếu trình duyệt giữ index cũ (cache heuristic)
                // người dùng chạy bundle cũ hàng giờ dù server đã có bản mới —
                // nguyên nhân "sửa rồi vẫn hỏng" qua tunnel.
                ctx.Context.Response.Headers.CacheControl = "no-cache";
            }
            else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            {
                // Tên file kèm hash nội dung — cache vĩnh viễn là an toàn.
                ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            }
        },
    });
}

app.UseCors("Spa");
app.UseAuthentication();
if (requireTeamLogin)
{
    // Mọi API trừ auth/health/dev phải có phiên (cookie StpTeam hoặc JWT dev).
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var bypass =
            path.StartsWithSegments("/api/auth")
            || path.StartsWithSegments("/api/health")
            || path.StartsWithSegments("/api/dev")
            || path.StartsWithSegments("/api/team/avatar")
            || path.StartsWithSegments("/api/team/avatars");
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        if (!authenticated)
        {
            // Scheme mặc định là JWT dev; người dùng nhóm có phiên cookie phải
            // authenticate riêng đúng scheme StpTeam.
            var teamResult = await context.AuthenticateAsync(StpTeamIdentity.SchemeName);
            authenticated = teamResult.Succeeded;
        }
        if (path.StartsWithSegments("/api") && !bypass && !authenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Cần đăng nhập",
                detail = "Phiên đăng nhập đã hết hoặc chưa đăng nhập — đăng nhập lại để tiếp tục.",
                status = 401
            });
            return;
        }

        await next();
    });
}
app.UseAuthorization();
app.MapControllers();
// F1: hub nhóm — /hubs/team (ngrok free hỗ trợ websocket, link tunnel bình thường).
app.MapHub<Stp.Api.Hubs.TeamHub>("/hubs/team");

// Trạng thái migration dùng cho health: migrate hỏng thì báo degraded nhưng
// service vẫn chạy (log lỗi chi tiết, không crash).
var startupMigrationError = (Exception?)null;

app.MapGet("/api/health", async (StpDbContext db, CancellationToken cancellationToken) =>
{
    var databaseOk = await db.Database.CanConnectAsync(cancellationToken);
    return Results.Ok(new
    {
        status = databaseOk && startupMigrationError is null ? "ok" : "degraded",
        database = databaseOk ? "connected" : "unavailable",
        databaseName = db.Database.GetDbConnection().Database,
        migrations = startupMigrationError is null ? "applied" : "failed",
        utc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/", (HttpContext http) =>
{
    // Có SPA build thì trả giao diện; chưa có thì redirect health như cũ.
    var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
    if (File.Exists(spaIndex))
    {
        // Giống UseStaticFiles bên trên: index.html luôn revalidate, không cache heuristic.
        http.Response.Headers.CacheControl = "no-cache";
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
    // Deep link cũng phải revalidate index — cùng lý do với UseStaticFiles.
    context.Response.Headers.CacheControl = "no-cache";
    return context.Response.SendFileAsync(spaIndex);
});

// Áp migration lúc khởi động (idempotent — chạy lại không làm gì). Repo trước
// đây KHÔNG tự chạy Database.Migrate: thêm bảng/cột mới mà quên áp tay thì API
// chạy trên schema cũ và chỉ vỡ lúc runtime. Migration hỏng chỉ log lỗi, không
// chặn service; health sẽ báo degraded cho đến khi khởi động lại thành công.
for (var attempt = 1; attempt <= 5; attempt++)
{
    try
    {
        using var migrateDb = app.Services.GetRequiredService<IDbContextFactory<StpDbContext>>().CreateDbContext();
        var pendingMigrations = new List<string>(
            await migrateDb.Database.GetPendingMigrationsAsync());
        await migrateDb.Database.MigrateAsync();
        if (pendingMigrations.Count > 0)
        {
            app.Logger.LogInformation(
                "Đã áp {Count} migration lúc khởi động: {Migrations}",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));
        }
        startupMigrationError = null;
        break;
    }
    catch (Exception migrateError) when (attempt < 5)
    {
        app.Logger.LogWarning("Chờ SQL Server sẵn sàng... thử lại lần {Attempt}/5 sau 2s", attempt);
        await Task.Delay(2000);
    }
    catch (Exception migrateError)
    {
        startupMigrationError = migrateError;
        app.Logger.LogError(
            migrateError,
            "Áp migration thất bại lúc khởi động — API vẫn chạy nhưng bảng/cột mới có thể thiếu. Kiểm tra kết nối SQL Server rồi khởi động lại.");
    }
}

// Seed tài khoản quản trị đầu tiên khi bảng Users còn trống. Mật khẩu mặc định
// chỉ ghi trong log khởi động — quản trị đổi ngay trong trang Quản lý tài khoản.
try
{
    using var seedDb = app.Services.GetRequiredService<IDbContextFactory<StpDbContext>>().CreateDbContext();
    if (!await seedDb.StpUsers.AsNoTracking().AnyAsync())
    {
        var admin = new StpUser
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Username = "admin",
            DisplayName = "Quản trị",
            Role = "admin",
            IsActive = true,
            PasswordHash = string.Empty
        };
        admin.PasswordHash = new PasswordHasher<StpUser>().HashPassword(admin, "stp2026");
        seedDb.StpUsers.Add(admin);
        await seedDb.SaveChangesAsync();
        app.Logger.LogWarning(
            "Đã tạo tài khoản quản trị mặc định admin / stp2026 — ĐỔI MẬT KHẨU NGAY trong trang Quản lý tài khoản.");
    }
}
catch (Exception seedError)
{
    app.Logger.LogWarning(
        seedError,
        "Chưa seed được tài khoản quản trị mặc định (bỏ qua, sẽ thử lại lần khởi động sau).");
}

app.Run();

public partial class Program;
