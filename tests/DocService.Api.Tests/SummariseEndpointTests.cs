using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace DocService.Api.Tests;

public class SummariseEndpointTests
{
    private const string Text = "Train 41 stopped at the platform. The doors failed to open. Passengers waited ten minutes.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Summarise_Valid_200_WithSources()
    {
        await using var factory = new DocServiceFactory();
        factory.Fake.Enqueue(TestData.SummaryJson);

        var response = await factory.CreateClient().PostAsJsonAsync("/summarise", new { text = Text, maxSentences = 2 }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal("Doors failed.", json["summary"]!.GetValue<string>());
        var source = json["sources"]![0]!;
        Assert.Equal(1, source["index"]!.GetValue<int>());
        Assert.Equal("The doors failed to open.", source["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Summarise_DefaultsMaxSentencesToThree()
    {
        await using var factory = new DocServiceFactory();
        factory.Fake.Enqueue(TestData.SummaryJson);

        await factory.CreateClient().PostAsJsonAsync("/summarise", new { text = Text }, Ct);

        Assert.Contains("at most 3 sentence", factory.Fake.Requests[0].SystemPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Summarise_MaxSentencesOutOfRange_400(int maxSentences)
    {
        await using var factory = new DocServiceFactory();

        var response = await factory.CreateClient().PostAsJsonAsync("/summarise", new { text = Text, maxSentences }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>(Ct))!;
        Assert.Equal(TestData.ProblemType("validation"), problem["type"]!.GetValue<string>());
        Assert.Empty(factory.Fake.Requests);
    }

    [Fact]
    public async Task Summarise_TooManySentences_413()
    {
        await using var factory = new DocServiceFactory();
        var text = string.Join(' ', Enumerable.Repeat("A.", 201));

        var response = await factory.CreateClient().PostAsJsonAsync("/summarise", new { text }, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
