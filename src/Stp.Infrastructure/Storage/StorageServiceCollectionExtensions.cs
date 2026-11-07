using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stp.Core.Storage;

namespace Stp.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStpStorage(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.PostConfigure<StorageOptions>(options =>
        {
            if (!Path.IsPathRooted(options.RootPath))
            {
                options.RootPath = Path.GetFullPath(
                    Path.Combine(contentRootPath, options.RootPath));
            }

            if (options.AllowedExtensions.Length == 0)
            {
                options.AllowedExtensions = new[] { ".xlsx", ".xlsm", ".xltx", ".xltm" };
            }
        });

        services.AddSingleton<IFileStorage, FileStorage>();
        return services;
    }
}
