using DocService.Core.Extraction;
using DocService.Core.Extraction.IncidentReports;
using DocService.Core.Llm;
using DocService.Core.Processing;
using DocService.Llm.Fake;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DocService.Core.Tests.Extraction;

public class ExtractionServiceTests
{
    private const string Valid =
        """{"date":"2026-03-03","location":"Entroncamento depot","equipment":["points machine P12"],"severity":"medium","summary":"Points failure delayed two services."}""";

    private const string FutureDate =
        """{"date":"2099-01-01","location":null,"equipment":[],"severity":"low","summary":"x"}""";

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    private static ExtractionService Create(
        FakeLlmClient fake,
        ProcessingOptions? options = null,
        PricingOptions? pricing = null,
        Microsoft.Extensions.Logging.ILogger<ExtractionService>? logger = null) =>
        new(
            fake,
            new ExtractionSchemaRegistry([new IncidentReportSchema(Clock)]),
            Options.Create(options ?? new ProcessingOptions()),
            new CostEstimator(Options.Create(pricing ?? new PricingOptions())),
            logger ?? NullLogger<ExtractionService>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Extract_ValidFirstAttempt_ReturnsData()
    {
        var fake = new FakeLlmClient().Enqueue(Valid);

        var result = await Create(fake).ExtractAsync("incident-report", "some text", Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(Severity.Medium, Assert.IsType<IncidentReport>(result.Value).Severity);
        var request = Assert.Single(fake.Requests);
        Assert.Equal("incident-report", request.ResponseSchema!.Name);
        Assert.Equal(800, request.MaxOutputTokens);
        Assert.Equal("some text", request.UserPrompt);
    }

    [Fact]
    public async Task Extract_InvalidThenValid_RepairsOnce()
    {
        var fake = new FakeLlmClient().Enqueue(FutureDate).Enqueue(Valid);

        var result = await Create(fake).ExtractAsync("incident-report", "some text", Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Attempts);
        Assert.Contains("date:", fake.Requests[1].UserPrompt, StringComparison.Ordinal);
        Assert.Contains("some text", fake.Requests[1].UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Extract_InvalidTwice_ReturnsInvalidOutput()
    {
        var fake = new FakeLlmClient().Enqueue(FutureDate).Enqueue("garbage");

        var result = await Create(fake).ExtractAsync("incident-report", "some text", Ct);

        Assert.Equal(ProcessingError.InvalidOutput, result.Error);
        Assert.Equal(2, result.Attempts);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task Extract_BudgetExhausted_SkipsRepair()
    {
        var fake = new FakeLlmClient().Enqueue("garbage", new LlmUsage(900, 50));
        var options = new ProcessingOptions { TokenBudgetPerRequest = 1000 };

        var result = await Create(fake, options).ExtractAsync("incident-report", "some text", Ct);

        Assert.Equal(ProcessingError.BudgetExhausted, result.Error);
        Assert.Equal(1, result.Attempts);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Extract_UnknownSchema_NoLlmCall()
    {
        var fake = new FakeLlmClient();

        var result = await Create(fake).ExtractAsync("invoice", "text", Ct);

        Assert.Equal(ProcessingError.UnknownSchema, result.Error);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Extract_TooLarge_NoLlmCall()
    {
        var fake = new FakeLlmClient();

        var result = await Create(fake).ExtractAsync("incident-report", new string('a', 20_001), Ct);

        Assert.Equal(ProcessingError.InputTooLarge, result.Error);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Extract_StripsCodeFences()
    {
        var fake = new FakeLlmClient().Enqueue($"```json\n{Valid}\n```");

        var result = await Create(fake).ExtractAsync("incident-report", "text", Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Extract_TruncatedOutput_NoRepair()
    {
        var fake = new FakeLlmClient().Enqueue("""{"date":"2026-03-""", finishReason: "length");

        var result = await Create(fake).ExtractAsync("incident-report", "text", Ct);

        Assert.Equal(ProcessingError.OutputTruncated, result.Error);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Extract_Cancelled_DoesNotRepair()
    {
        var fake = new FakeLlmClient().EnqueueHang();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(fake).ExtractAsync("incident-report", "text", cts.Token));

        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Extract_UsageSummedAcrossAttempts()
    {
        var fake = new FakeLlmClient()
            .Enqueue(FutureDate, new LlmUsage(100, 50))
            .Enqueue(Valid, new LlmUsage(120, 60));

        var result = await Create(fake).ExtractAsync("incident-report", "text", Ct);

        Assert.Equal(220, result.Usage.InputTokens);
        Assert.Equal(110, result.Usage.OutputTokens);
    }

    [Fact]
    public async Task Extract_EstimatesCost()
    {
        var fake = new FakeLlmClient().Enqueue(Valid, new LlmUsage(1_000_000, 500_000));
        var pricing = new PricingOptions { InputPerMillionUsd = 1.00m, OutputPerMillionUsd = 2.00m };

        var result = await Create(fake, pricing: pricing).ExtractAsync("incident-report", "text", Ct);

        Assert.Equal(2.00m, result.Usage.EstimatedCostUsd);
    }

    [Fact]
    public async Task Extract_NeverLogsInputText()
    {
        var logger = new Microsoft.Extensions.Logging.Testing.FakeLogger<ExtractionService>();
        var fake = new FakeLlmClient().Enqueue(Valid);

        await Create(fake, logger: logger).ExtractAsync("incident-report", "SENTINEL-TEXT-42", Ct);

        var records = logger.Collector.GetSnapshot();
        Assert.NotEmpty(records);
        Assert.DoesNotContain(records, r => r.Message.Contains("SENTINEL-TEXT-42", StringComparison.Ordinal));
    }
}
