using System.Net;
using System.Text.Json.Nodes;
using DocService.Core.Extraction.IncidentReports;
using DocService.Core.Llm;
using DocService.Core.Processing;
using DocService.Core.Summaries;
using DocService.Llm;
using DocService.Llm.Fake;
using DocService.Llm.OpenAiCompatible;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DocService.Core.Tests.Llm;

public class OpenAiCompatibleLlmClientTests
{
    private const string GroqReply =
        """{"model":"openai/gpt-oss-20b","choices":[{"message":{"role":"assistant","content":"{\"ok\":true}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":412,"completion_tokens":96,"total_tokens":508}}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OpenAiCompatibleLlmClient Create(HttpMessageHandler handler, LlmOptions? options = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.groq.com/openai/v1/") },
            Options.Create(options ?? new LlmOptions { ApiKey = "test-key" }),
            TimeProvider.System);

    private static LlmRequest Request() => new(
        "system text",
        "user text",
        new LlmJsonSchema("incident-report", new JsonObject { ["type"] = "object" }),
        800);

    [Fact]
    public async Task SendsSchemaAndLimits()
    {
        var handler = StubHttpMessageHandler.Json(GroqReply);

        await Create(handler).CompleteAsync(Request(), Ct);

        var (request, body) = Assert.Single(handler.Calls);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", request.Headers.Authorization!.ToString());
        var json = JsonNode.Parse(body)!;
        Assert.Equal("openai/gpt-oss-20b", json["model"]!.GetValue<string>());
        Assert.Equal(800, json["max_tokens"]!.GetValue<int>());
        Assert.Equal("low", json["reasoning_effort"]!.GetValue<string>());
        Assert.Equal("system", json["messages"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("user text", json["messages"]![1]!["content"]!.GetValue<string>());
        Assert.Equal("json_schema", json["response_format"]!["type"]!.GetValue<string>());
        Assert.True(json["response_format"]!["json_schema"]!["strict"]!.GetValue<bool>());
        Assert.Equal("incident-report", json["response_format"]!["json_schema"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task OmitsOptionalFieldsWhenNotConfigured()
    {
        var handler = StubHttpMessageHandler.Json(GroqReply);
        var options = new LlmOptions { ApiKey = "k", ReasoningEffort = null };

        await Create(handler, options).CompleteAsync(Request() with { ResponseSchema = null }, Ct);

        var json = JsonNode.Parse(handler.Calls[0].Body)!.AsObject();
        Assert.False(json.ContainsKey("reasoning_effort"));
        Assert.False(json.ContainsKey("response_format"));
    }

    [Fact]
    public async Task ParsesContentUsageAndFinishReason()
    {
        var result = await Create(StubHttpMessageHandler.Json(GroqReply)).CompleteAsync(Request(), Ct);

        Assert.Equal("""{"ok":true}""", result.Content);
        Assert.Equal(new LlmUsage(412, 96), result.Usage);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal("openai/gpt-oss-20b", result.Model);
    }

    [Fact]
    public async Task TooManyRequests_MapsToRateLimited_WithRetryAfter()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        });

        var ex = await Assert.ThrowsAsync<LlmRateLimitedException>(() => Create(handler).CompleteAsync(Request(), Ct));

        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Unauthorized_MapsToProviderException_NoRetry(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Json("{}", status);

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => Create(handler).CompleteAsync(Request(), Ct));

        Assert.Equal((int)status, ex.StatusCode);
        Assert.Contains("API key", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task ServerError_MapsToProviderException()
    {
        var handler = StubHttpMessageHandler.Json("{}", HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<LlmProviderException>(() => Create(handler).CompleteAsync(Request(), Ct));

        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task EmptyChoices_MapsToProviderException()
    {
        var handler = StubHttpMessageHandler.Json("""{"choices":[]}""");

        await Assert.ThrowsAsync<LlmProviderException>(() => Create(handler).CompleteAsync(Request(), Ct));
    }

    [Fact]
    public async Task CallerCancellation_Rethrows_OperationCanceled()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).CompleteAsync(Request(), cts.Token));
    }

    [Fact]
    public async Task HttpClientTimeout_MapsToLlmTimeout()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<LlmTimeoutException>(() => Create(handler).CompleteAsync(Request(), Ct));
    }

    [Fact]
    public async Task ResilienceTimeout_MapsToLlmTimeout()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new TimeoutRejectedException("pipeline timeout"));

        await Assert.ThrowsAsync<LlmTimeoutException>(() => Create(handler).CompleteAsync(Request(), Ct));
    }

    [Fact]
    public async Task OpenCircuit_MapsToLlmUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new BrokenCircuitException("open"));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => Create(handler).CompleteAsync(Request(), Ct));
    }

    [Fact]
    public async Task NetworkFailure_MapsToLlmUnavailable()
    {
        var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("dns"));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => Create(handler).CompleteAsync(Request(), Ct));
    }
}

public class LlmOptionsValidatorTests
{
    private readonly LlmOptionsValidator _validator = new();

    [Fact]
    public void Validator_MissingKey_Fails_WithHelpfulMessage()
    {
        var result = _validator.Validate(null, new LlmOptions { Provider = LlmProvider.OpenAiCompatible, ApiKey = null });

        Assert.True(result.Failed);
        Assert.Contains("Llm:ApiKey is not set", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("user-secrets", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_FakeProvider_NoKeyNeeded() =>
        Assert.True(_validator.Validate(null, new LlmOptions { Provider = LlmProvider.Fake }).Succeeded);

    [Fact]
    public void Validator_BaseUrlWithoutTrailingSlash_Fails()
    {
        var options = new LlmOptions { ApiKey = "k", BaseUrl = new Uri("https://api.groq.com/openai/v1") };

        Assert.True(_validator.Validate(null, options).Failed);
    }

    [Fact]
    public void Validator_Defaults_WithKey_Succeed() =>
        Assert.True(_validator.Validate(null, new LlmOptions { ApiKey = "k" }).Succeeded);
}

public class CannedResponderTests
{
    [Fact]
    public void CannedResponder_IncidentReport_IsValidForSchema()
    {
        var result = CannedResponder.Respond(new LlmRequest("s", "u", new IncidentReportSchema(TimeProvider.System).ResponseSchema, 800));

        var parsed = new IncidentReportSchema(TimeProvider.System).Parse(result.Content);
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
    }

    [Fact]
    public async Task CannedResponder_Summary_IsValidForSummaryService()
    {
        var fake = new FakeLlmClient { Responder = CannedResponder.Respond };
        var service = new SummaryService(
            fake,
            Options.Create(new ProcessingOptions()),
            new CostEstimator(Options.Create(new PricingOptions())),
            NullLogger<SummaryService>.Instance);

        var result = await service.SummariseAsync("One sentence only.", 3, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors));
    }
}
