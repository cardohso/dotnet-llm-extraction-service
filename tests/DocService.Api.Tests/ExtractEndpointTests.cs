using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DocService.Core.Llm;

namespace DocService.Api.Tests;

public class ExtractEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> Extract(DocServiceFactory factory, string schema, string text) =>
        factory.CreateClient().PostAsJsonAsync("/extract", new { schema, text }, Ct);

    [Fact]
    public async Task Extract_Valid_200()
    {
        await using var factory = new DocServiceFactory();
        factory.Fake.Enqueue(TestData.IncidentJson);

        var response = await Extract(factory, "incident-report", "Points failed at Entroncamento.");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal("incident-report", json["schema"]!.GetValue<string>());
        Assert.Equal("medium", json["data"]!["severity"]!.GetValue<string>());
        Assert.Equal("2026-03-03", json["data"]!["date"]!.GetValue<string>());
        Assert.Equal(1, json["attempts"]!.GetValue<int>());
        Assert.Equal(100, json["usage"]!["inputTokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Extract_UnknownSchema_400_ProblemDetails()
    {
        await using var factory = new DocServiceFactory();

        var response = await Extract(factory, "invoice", "text");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TestData.ProblemJsonType, response.Content.Headers.ContentType!.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(TestData.ProblemType("validation"), problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["traceId"]);
        Assert.Contains("incident-report", problem["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task Extract_BlankText_400(string text)
    {
        await using var factory = new DocServiceFactory();

        var response = await Extract(factory, "incident-report", text);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(TestData.ProblemType("validation"), problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["errors"]);
    }

    [Fact]
    public async Task Extract_MissingSchema_400()
    {
        await using var factory = new DocServiceFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/extract", new { text = "x" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_400_ProblemDetails()
    {
        await using var factory = new DocServiceFactory();

        var response = await factory.CreateClient().PostAsync(
            "/extract", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TestData.ProblemJsonType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Extract_TooLarge_413()
    {
        await using var factory = new DocServiceFactory();

        var response = await Extract(factory, "incident-report", new string('a', 20_001));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(TestData.ProblemType("input-too-large"), problem["type"]!.GetValue<string>());
        Assert.Empty(factory.Fake.Requests);
    }

    [Fact]
    public async Task Extract_InvalidTwice_422_WithErrors()
    {
        await using var factory = new DocServiceFactory();
        factory.Fake.Enqueue("garbage").Enqueue("still garbage");

        var response = await Extract(factory, "incident-report", "text");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(TestData.ProblemType("llm-output-invalid"), problem["type"]!.GetValue<string>());
        Assert.Equal("invalid-output", problem["reason"]!.GetValue<string>());
        Assert.NotEmpty(problem["errors"]!.AsArray());
    }

    [Fact]
    public async Task Extract_PortugueseText_RoundTripsUtf8()
    {
        await using var factory = new DocServiceFactory();
        const string text = "Falha no aparelho de mudança de via em Coimbra-B.";
        factory.Fake.Enqueue(TestData.IncidentJson.Replace("points machine P12", "aparelho de mudança de via", StringComparison.Ordinal));

        var response = await Extract(factory, "incident-report", text);

        var json = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal("aparelho de mudança de via", json["data"]!["equipment"]![0]!.GetValue<string>());
        Assert.Equal(text, factory.Fake.Requests[0].UserPrompt);
        Assert.Contains("mudança", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task ProviderFailure_MapsToStatus(Exception failure, HttpStatusCode expected, string typeSuffix)
    {
        await using var factory = new DocServiceFactory();
        factory.Fake.EnqueueException(failure);

        var response = await Extract(factory, "incident-report", "text");

        Assert.Equal(expected, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(TestData.ProblemType(typeSuffix), problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["traceId"]);
    }

    public static TheoryData<Exception, HttpStatusCode, string> ProviderFailures() => new()
    {
        { new LlmProviderException("Provider returned HTTP 500", 500), HttpStatusCode.BadGateway, "provider-error" },
        { new LlmUnavailableException("Provider circuit breaker is open"), HttpStatusCode.ServiceUnavailable, "provider-unavailable" },
        { new LlmTimeoutException("Provider call timed out"), HttpStatusCode.GatewayTimeout, "provider-timeout" },
    };

    [Fact]
    public async Task ProviderRateLimited_503_WithRetryAfter()
    {
        await using var factory = new DocServiceFactory();
        factory.Fake.EnqueueException(new LlmRateLimitedException("Provider rate limit exceeded", TimeSpan.FromSeconds(7)));

        var response = await Extract(factory, "incident-report", "text");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(7), response.Headers.RetryAfter!.Delta);
    }

    [Fact]
    public async Task UnexpectedException_500_ProblemDetails_WithoutLeakingMessage()
    {
        await using var factory = new DocServiceFactory();
        factory.Fake.EnqueueException(new InvalidOperationException("secret internal detail"));

        var response = await Extract(factory, "incident-report", "text");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret internal detail", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
