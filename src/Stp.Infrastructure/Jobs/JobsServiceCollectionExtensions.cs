using Microsoft.Extensions.DependencyInjection;
using Stp.Core.Jobs;

namespace Stp.Infrastructure.Jobs;

public static class JobsServiceCollectionExtensions
{
    public static IServiceCollection AddStpJobs(this IServiceCollection services)
    {
        services.AddSingleton<IBatchJobQueue, BatchJobQueue>();
        services.AddScoped<IBatchJobProcessor, BatchJobProcessor>();
        services.AddHostedService<BatchJobWorker>();
        return services;
    }
}
