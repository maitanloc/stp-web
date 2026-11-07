using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Stp.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Server=localhost;Database=StpArchive;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public static IServiceCollection AddStpPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("StpArchive") ?? DefaultConnectionString;

        services.AddDbContext<StpDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(StpDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            }));

        return services;
    }
}
