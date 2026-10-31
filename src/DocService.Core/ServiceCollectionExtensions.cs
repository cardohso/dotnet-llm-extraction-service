using DocService.Core.Extraction;
using DocService.Core.Extraction.IncidentReports;
using DocService.Core.Processing;
using DocService.Core.Summaries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocService.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocServiceCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<PricingOptions>()
            .Bind(configuration.GetSection(PricingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IExtractionSchema, IncidentReportSchema>();
        services.AddSingleton<ExtractionSchemaRegistry>();
        services.AddSingleton<CostEstimator>();
        services.AddSingleton<ExtractionService>();
        services.AddSingleton<SummaryService>();
        return services;
    }
}
