using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace DocService.Api.Tests;

public class RateLimitingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RateLimit_SixthRapidRequest_429()
    {
        await using var factory = new DocServiceFactory();
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            var ok = await client.PostAsJsonAsync("/extract", new { schema = "invoice", text = "x" }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, ok.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/extract", new { schema = "invoice", text = "x" }, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        var problem = (await limited.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(TestData.ProblemType("rate-limited"), problem["type"]!.GetValue<string>());
        Assert.NotNull(problem["traceId"]);
    }

    [Fact]
    public async Task RateLimit_DoesNotApplyToHealthOrOpenApi()
    {
        await using var factory = new DocServiceFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", Ct)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json", Ct)).StatusCode);
    }

    [Fact]
    public async Task ConcurrencyLimit_SecondInFlightRequest_429()
    {
        await using var factory = new DocServiceFactory()
            .WithSetting("RateLimiting:ConcurrencyLimit", "1")
            .WithSetting("RateLimiting:QueueLimit", "0");
        factory.Fake.EnqueueHang();
        var client = factory.CreateClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var first = client.PostAsJsonAsync("/extract", new { schema = "incident-report", text = "x" }, cts.Token);
        while (factory.Fake.Requests.Count == 0)
        {
            await Task.Delay(10, Ct);
        }

        var second = await client.PostAsJsonAsync("/extract", new { schema = "incident-report", text = "y" }, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }
}
