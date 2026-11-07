using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

public static class ExcelServiceCollectionExtensions
{
    public static IServiceCollection AddStpExcel(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<ExcelComOptions>(configuration.GetSection("ExcelCom"));
            services.Configure<VbaInjectionOptions>(configuration.GetSection("ExcelVba"));
        }
        else
        {
            services.AddOptions<ExcelComOptions>();
            services.AddOptions<VbaInjectionOptions>();
        }

        services.AddSingleton<IExcelInspector, ExcelInspector>();
        services.AddSingleton<VbaProjectInjector>();
        services.AddSingleton<IExcelConversionService, ComExcelEngine>();
        services.AddSingleton<MacroTemplateProvider>();
        return services;
    }
}
