using System;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stp.Core.Summary;
using Stp.Core.Sync;

namespace Stp.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Server=localhost;Database=StpArchive;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public static IServiceCollection AddStpPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rawConnectionString = configuration.GetConnectionString("StpArchive") ?? DefaultConnectionString;
        var connectionString = ResolveValidConnectionString(rawConnectionString);

        // DbContextFactory thay cho AddDbContext: singleton ISummaryToolService cần
        // mở DbContext riêng (CatalogStore) thay vì mượn scoped.
        services.AddDbContextFactory<StpDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(StpDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            }));

        services.AddScoped<IArchiveSyncService, ArchiveSyncService>();
        services.AddSingleton<ICatalogDictionary, CatalogStore>();
        services.AddSingleton<ICatalogPresetService, CatalogPresetService>();
        return services;
    }

    /// <summary>
    /// Tự động phát hiện và chuyển đổi giữa localhost (máy chính/service) và .\SQLEXPRESS (máy dev)
    /// để tránh xung đột cấu hình giữa các thành viên nhóm khi push git.
    /// </summary>
    private static string ResolveValidConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 2
            };
            using var conn = new SqlConnection(builder.ConnectionString);
            conn.Open();
            // Kết nối thành công theo cấu hình hiện tại
            return connectionString;
        }
        catch
        {
            try
            {
                var fallbackBuilder = new SqlConnectionStringBuilder(connectionString);
                if (fallbackBuilder.DataSource.Contains("SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackBuilder.DataSource = "localhost";
                }
                else
                {
                    fallbackBuilder.DataSource = @".\SQLEXPRESS";
                }
                fallbackBuilder.ConnectTimeout = 2;
                using var fallbackConn = new SqlConnection(fallbackBuilder.ConnectionString);
                fallbackConn.Open();

                // Fallback thành công! Phục hồi timeout mặc định
                fallbackBuilder.ConnectTimeout = 15;
                return fallbackBuilder.ConnectionString;
            }
            catch
            {
                return connectionString;
            }
        }
    }
}
