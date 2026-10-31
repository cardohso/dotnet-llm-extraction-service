using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using DocService.Core.Llm;
using DocService.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocService.Core.Tests.Llm;

public class ResilienceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly LlmRequest Request = new("s", "u", null, 100);

    private const string OkReply =
        """{"choices":[{"message":{"content":"{}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";

    private static ServiceProvider Build(StubHttpMessageHandler stub, Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Llm:ApiKey"] = "test-key",
            ["Llm:Resilience:Retry:Delay"] = "00:00:00.010",
            ["Llm:Resilience:Retry:UseJitter"] = "false",
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddLlmClient(configuration)!.ConfigurePrimaryHttpMessageHandler(() => stub);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ServerError_RetriedTwice_ThenProviderException()
    {
        var stub = StubHttpMessageHandler.Json("{}", HttpStatusCode.InternalServerError);
        using var provider = Build(stub);

        await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.GetRequiredService<ILlmClient>().CompleteAsync(Request, Ct));

        Assert.Equal(3, stub.Calls.Count);
    }

    [Fact]
    public async Task Unauthorized_NotRetried()
    {
        var stub = StubHttpMessageHandler.Json("{}", HttpStatusCode.Unauthorized);
        using var provider = Build(stub);

        var ex = await Assert.ThrowsAsync<LlmProviderException>(
            () => provider.GetRequiredService<ILlmClient>().CompleteAsync(Request, Ct));

        Assert.Equal(401, ex.StatusCode);
        Assert.Single(stub.Calls);
    }

    [Fact]
    public async Task RetryAfter_Honoured()
    {
        var attempts = 0;
        var stub = new StubHttpMessageHandler((_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return Task.FromResult(limited);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OkReply, System.Text.Encoding.UTF8, "application/json"),
            });
        });
        using var provider = Build(stub);

        var started = Stopwatch.GetTimestamp();
        var result = await provider.GetRequiredService<ILlmClient>().CompleteAsync(Request, Ct);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal("{}", result.Content);
        Assert.Equal(2, stub.Calls.Count);
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(900), $"expected the 1 s Retry-After to be honoured, waited {elapsed}");
    }

    [Fact]
    public async Task Breaker_OpensAfterRepeatedFailures_ThenFailsFast()
    {
        var stub = StubHttpMessageHandler.Json("{}", HttpStatusCode.InternalServerError);
        using var provider = Build(stub, new()
        {
            ["Llm:Resilience:Retry:MaxRetryAttempts"] = "1",
            ["Llm:Resilience:CircuitBreaker:MinimumThroughput"] = "2",
        });
        var client = provider.GetRequiredService<ILlmClient>();
        var circuit = provider.GetRequiredService<ILlmCircuitState>();
        Assert.False(circuit.IsOpen);

        await Assert.ThrowsAsync<LlmProviderException>(() => client.CompleteAsync(Request, Ct));
        var callsBefore = stub.Calls.Count;

        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.CompleteAsync(Request, Ct));

        Assert.True(circuit.IsOpen);
        Assert.Equal(callsBefore, stub.Calls.Count);
    }
}
