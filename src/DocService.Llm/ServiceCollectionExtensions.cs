using DocService.Core.Llm;
using DocService.Llm.Fake;
using DocService.Llm.OpenAiCompatible;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocService.Llm;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ILlmClient"/> for the provider chosen in the <c>Llm</c> section.
    /// Returns the HTTP client builder for the real provider (so callers can add handlers), or null for the fake.
    /// </summary>
    public static IHttpClientBuilder? AddLlmClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(LlmOptions.SectionName);
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();
        services.AddOptions<LlmOptions>().Bind(section).ValidateOnStart();

        var provider = section.Get<LlmOptions>()?.Provider ?? LlmProvider.OpenAiCompatible;
        if (provider == LlmProvider.Fake)
        {
            services.AddSingleton<ILlmClient>(_ => new FakeLlmClient { Responder = CannedResponder.Respond });
            return null;
        }

        return services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>((serviceProvider, client) =>
            client.BaseAddress = serviceProvider.GetRequiredService<IOptions<LlmOptions>>().Value.BaseUrl);
    }
}
