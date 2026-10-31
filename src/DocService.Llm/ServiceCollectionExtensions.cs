using System.Net;
using DocService.Core.Llm;
using DocService.Llm.Fake;
using DocService.Llm.OpenAiCompatible;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace DocService.Llm;

public static class ServiceCollectionExtensions
{
    /// <summary>A provider 429 asking us to wait longer than this is returned to the caller instead of retried.</summary>
    private static readonly TimeSpan MaxRetryAfterWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers <see cref="ILlmClient"/> for the provider chosen in the <c>Llm</c> section.
    /// Returns the HTTP client builder for the real provider (so callers can add handlers), or null for the fake.
    /// Resilience (timeouts, retries, circuit breaker) can be tuned under <c>Llm:Resilience</c>.
    /// </summary>
    /// <exception cref="OptionsValidationException">Llm:Provider is not a known provider.</exception>
    public static IHttpClientBuilder? AddLlmClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(LlmOptions.SectionName);
        var provider = ReadProvider(section);
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();
        services.AddOptions<LlmOptions>().Bind(section).ValidateOnStart();

        if (provider == LlmProvider.Fake)
        {
            services.AddSingleton<ILlmClient>(_ => new FakeLlmClient { Responder = CannedResponder.Respond });
            return null;
        }

        services.TryAddSingleton(TimeProvider.System);

        var circuit = new LlmCircuitState();
        services.AddSingleton<ILlmCircuitState>(circuit);

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
                options.Retry.ShouldHandle = args => ValueTask.FromResult(ShouldRetry(args.Outcome));
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.MinimumThroughput = 5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.OnOpened = _ => SetCircuit(circuit, isOpen: true);
                options.CircuitBreaker.OnHalfOpened = _ => SetCircuit(circuit, isOpen: false);
                options.CircuitBreaker.OnClosed = _ => SetCircuit(circuit, isOpen: false);
            });

        var overrides = section.GetSection("Resilience");
        if (overrides.Exists())
        {
            pipeline.Configure(overrides);
        }

        return httpClient;
    }

    private static LlmProvider ReadProvider(IConfigurationSection section)
    {
        var value = section["Provider"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return LlmProvider.OpenAiCompatible;
        }

        if (Enum.TryParse<LlmProvider>(value, ignoreCase: true, out var provider) && Enum.IsDefined(provider)
            && !int.TryParse(value, out _))
        {
            return provider;
        }

        throw new OptionsValidationException(
            LlmOptions.SectionName,
            typeof(LlmOptions),
            [$"Llm:Provider '{value}' is not valid. Use OpenAiCompatible or Fake."]);
    }

    /// <summary>Transient failures are retried, except a 429 whose Retry-After is too long to wait out within the request.</summary>
    private static bool ShouldRetry(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Result is { StatusCode: HttpStatusCode.TooManyRequests } response)
        {
            var header = response.Headers.RetryAfter;
            var wait = header?.Delta ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (wait > MaxRetryAfterWait)
            {
                return false;
            }
        }

        return HttpClientResiliencePredicates.IsTransient(outcome);
    }

    private static ValueTask SetCircuit(LlmCircuitState circuit, bool isOpen)
    {
        circuit.Set(isOpen);
        return ValueTask.CompletedTask;
    }
}
