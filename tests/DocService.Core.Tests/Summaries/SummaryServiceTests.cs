using DocService.Core.Llm;
using DocService.Core.Processing;
using DocService.Core.Summaries;
using DocService.Llm.Fake;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocService.Core.Tests.Summaries;

public class SummaryServiceTests
{
    private const string Text = "Train 41 stopped at the platform. The doors failed to open. Passengers waited ten minutes.";

    private static SummaryService Create(FakeLlmClient fake, ProcessingOptions? options = null) =>
        new(
            fake,
            Options.Create(options ?? new ProcessingOptions()),
            new CostEstimator(Options.Create(new PricingOptions())),
            NullLogger<SummaryService>.Instance);

    private static string Reply(string summary, params int[] indices) =>
        $$"""{"summary":"{{summary}}","sourceIndices":[{{string.Join(',', indices)}}]}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Summarise_ReturnsVerbatimSources()
    {
        var fake = new FakeLlmClient().Enqueue(Reply("Doors failed.", 2, 0));

        var result = await Create(fake).SummariseAsync(Text, 3, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("Doors failed.", result.Value!.Summary);
        Assert.Equal(
            [new SourceSentence(0, "Train 41 stopped at the platform."), new SourceSentence(2, "Passengers waited ten minutes.")],
            result.Value.Sources);
        var request = Assert.Single(fake.Requests);
        Assert.Contains("[1] The doors failed to open.", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summarise_OutOfRangeIndex_Repairs()
    {
        var fake = new FakeLlmClient().Enqueue(Reply("x", 9)).Enqueue(Reply("Doors failed.", 1));

        var result = await Create(fake).SummariseAsync(Text, 3, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Attempts);
        Assert.Contains("sourceIndices", fake.Requests[1].UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Summarise_TooManyIndices_Repairs()
    {
        var fake = new FakeLlmClient().Enqueue(Reply("x", 0, 1)).Enqueue(Reply("x", 0));

        var result = await Create(fake).SummariseAsync(Text, 1, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Attempts);
    }

    [Fact]
    public async Task Summarise_SingleSentence_Works()
    {
        var fake = new FakeLlmClient().Enqueue(Reply("It stopped.", 0));

        var result = await Create(fake).SummariseAsync("The train stopped.", 3, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([new SourceSentence(0, "The train stopped.")], result.Value!.Sources);
    }

    [Fact]
    public async Task Summarise_MaxOutputTokensIs600()
    {
        var fake = new FakeLlmClient().Enqueue(Reply("x", 0));

        await Create(fake).SummariseAsync(Text, 3, Ct);

        Assert.Equal(600, fake.Requests[0].MaxOutputTokens);
        Assert.Equal("summary-with-sources", fake.Requests[0].ResponseSchema!.Name);
    }

    [Fact]
    public async Task Summarise_Over200Sentences_InputTooLarge()
    {
        var fake = new FakeLlmClient();
        var text = string.Join(' ', Enumerable.Repeat("A.", 201));

        var result = await Create(fake).SummariseAsync(text, 3, Ct);

        Assert.Equal(ProcessingError.InputTooLarge, result.Error);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Summarise_InvalidTwice_ReturnsInvalidOutput()
    {
        var fake = new FakeLlmClient().Enqueue("nope").Enqueue(Reply("", 0));

        var result = await Create(fake).SummariseAsync(Text, 3, Ct);

        Assert.Equal(ProcessingError.InvalidOutput, result.Error);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task Summarise_TruncatedOutput_NoRepair()
    {
        var fake = new FakeLlmClient().Enqueue("{\"summary\":\"cut", finishReason: "length");

        var result = await Create(fake).SummariseAsync(Text, 3, Ct);

        Assert.Equal(ProcessingError.OutputTruncated, result.Error);
        Assert.Single(fake.Requests);
    }
}
