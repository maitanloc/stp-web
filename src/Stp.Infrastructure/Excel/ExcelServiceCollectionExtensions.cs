using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Stp.Core.Excel;
using Stp.Core.Macros;
using Stp.Infrastructure.Macros;

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
            services.Configure<PrintLayoutOptions>(configuration.GetSection("PrintLayout"));
        }
        else
        {
            services.AddOptions<ExcelComOptions>();
            services.AddOptions<VbaInjectionOptions>();
            services.AddOptions<PrintLayoutOptions>();
        }

        services.AddSingleton<IExcelInspector, ExcelInspector>();
        services.AddSingleton<VbaProjectInjector>();
        services.AddSingleton(sp => new PrintLayoutService(
            sp.GetRequiredService<IOptions<PrintLayoutOptions>>().Value));
        services.AddSingleton<IPrintSheetGenerator, Type2SheetGenerator>();
        services.AddSingleton<IMergedWorkbookService, MergedWorkbookService>();
        services.AddSingleton<IExcelConversionService, ComExcelEngine>();
        services.AddSingleton<MacroTemplateProvider>();
        services.AddScoped<IMacroService, MacroService>();
        return services;
    }
}
