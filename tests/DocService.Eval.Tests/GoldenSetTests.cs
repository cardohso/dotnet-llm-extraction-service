using DocService.Core.Extraction;
using DocService.Core.Extraction.IncidentReports;
using DocService.Core.Processing;
using DocService.Eval;
using DocService.Llm.Fake;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocService.Eval.Tests;

public class GoldenSetTests
{
    private static string GoldenPath => Path.Combine(RepoRoot.Find(), "eval", "golden", "incident-reports.jsonl");

    [Fact]
    public void GoldenFile_IsWellFormed()
    {
        var cases = GoldenLoader.Load(GoldenPath);

        Assert.Equal(18, cases.Count);
        Assert.Equal(cases.Count, cases.Select(c => c.Id).Distinct().Count());
        Assert.All(cases, c =>
        {
            Assert.True(c.Text.Length is > 0 and <= 20_000, c.Id);
            Assert.True(Enum.TryParse<Severity>(c.Expected.Severity, ignoreCase: true, out _), c.Id);
            Assert.True(c.Expected.Date is null || DateOnly.TryParseExact(c.Expected.Date, "yyyy-MM-dd", out _), c.Id);
        });
        Assert.Equal(3, cases.Count(c => c.Lang == "pt"));
    }

    [Fact]
    public void Loader_DuplicateIds_Throws()
    {
        var path = Path.GetTempFileName();
        var line = """{"id":"x","lang":"en","text":"t","expected":{"date":null,"location":null,"equipment":[],"severity":"low"}}""";
        File.WriteAllLines(path, [line, line]);
        try
        {
            Assert.Throws<InvalidDataException>(() => GoldenLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GoldenResponder_ThroughExtractionService_ScoresPerfect()
    {
        var cases = GoldenLoader.Load(GoldenPath);
        var fake = new FakeLlmClient { Responder = GoldenResponder.For(cases) };
        var service = new ExtractionService(
            fake,
            new ExtractionSchemaRegistry([new IncidentReportSchema(TimeProvider.System)]),
            Options.Create(new ProcessingOptions()),
            new CostEstimator(Options.Create(new PricingOptions())),
            NullLogger<ExtractionService>.Instance);

        var results = new List<CaseResult>();
        foreach (var golden in cases)
        {
            results.Add(await EvalRunner.RunCaseAsync(service, golden, TimeSpan.Zero, TestContext.Current.CancellationToken));
        }

        var summary = EvalSummary.From(results);
        Assert.Equal(1.0, summary.ValidAfterRepairRate);
        Assert.Equal(1.0, summary.FirstAttemptValidRate);
        Assert.Equal(1.0, summary.OverallFieldAccuracy);
        Assert.Equal(1.0, summary.SeverityAccuracy);
    }

    [Fact]
    public void Thresholds_FileMatchesPreRegisteredValues()
    {
        var limits = Thresholds.Load(Path.Combine(RepoRoot.Find(), "eval", "thresholds.json"));

        Assert.Equal(new Thresholds(0.95, 0.85, 0.80, 5000), limits);
    }
}
