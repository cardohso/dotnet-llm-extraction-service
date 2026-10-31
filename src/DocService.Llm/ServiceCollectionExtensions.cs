using DocService.Core.Llm;
using DocService.Llm.Fake;
using DocService.Llm.OpenAiCompatible;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace DocService.Llm;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ILlmClient"/> for the provider chosen in the <c>Llm</c> section.
    /// Returns the HTTP client builder for the real provider (so callers can add handlers), or null for the fake.
    /// Resilience (timeouts, retries, circuit breaker) can be tuned under <c>Llm:Resilience</c>.
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

        services.TryAddSingleton(TimeProvider.System);

        var circuit = new CircuitBreakerStateProvider();
        services.AddSingleton(circuit);
        services.AddSingleton<ILlmCircuitState>(new PollyCircuitState(circuit));

        var httpClient = services.AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>((serviceProvider, client) =>
            client.BaseAddress = serviceProvider.GetRequiredService<IOptions<LlmOptions>>().Value.BaseUrl);

        var pipeline = httpClient
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);
                options.Retry.UseJitter = true;
                options.Retry.ShouldRetryAfterHeader = true;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.StateProvider = circuit;
            });

        var overrides = section.GetSection("Resilience");
        if (overrides.Exists())
        {
            pipeline.Configure(overrides);
        }

        return httpClient;
    }
}
