using DocService.Eval;

namespace DocService.Eval.Tests;

public class EvalReportTests
{
    private static CaseResult Case(
        string id,
        bool valid = true,
        int attempts = 1,
        long latencyMs = 100,
        bool date = true,
        bool location = true,
        double equipment = 1.0,
        bool severity = true) =>
        new(id, "en", valid, attempts, latencyMs, 150, valid ? null : "invalid-output", date, location, equipment, severity);

    private static readonly Thresholds Limits = new(0.95, 0.85, 0.80, 5000);

    [Fact]
    public void Percentile_NearestRank()
    {
        var values = Enumerable.Range(1, 10).Select(i => i * 100L).ToArray();

        Assert.Equal(1000, Percentile.NearestRank(values, 95));
        Assert.Equal(500, Percentile.NearestRank(values, 50));
        Assert.Equal(100, Percentile.NearestRank([100], 95));
    }

    [Fact]
    public void Summary_InvalidCase_ScoresZero()
    {
        var summary = EvalSummary.From([Case("a"), Case("b", valid: false, date: false, location: false, equipment: 0, severity: false)]);

        Assert.Equal(0.5, summary.ValidAfterRepairRate);
        Assert.Equal(0.5, summary.DateAccuracy);
        Assert.Equal(0.5, summary.SeverityAccuracy);
        Assert.Equal(0.5, summary.OverallFieldAccuracy);
    }

    [Fact]
    public void Summary_FirstAttemptVersusAfterRepair()
    {
        var summary = EvalSummary.From([Case("a"), Case("b", attempts: 2), Case("c", valid: false, attempts: 2)]);

        Assert.Equal(1.0 / 3.0, summary.FirstAttemptValidRate, 3);
        Assert.Equal(2.0 / 3.0, summary.ValidAfterRepairRate, 3);
    }

    [Fact]
    public void Summary_OverallIsMeanOfFourFieldScores()
    {
        var summary = EvalSummary.From([Case("a", date: false, equipment: 0.5)]);

        // date 0, location 1, equipment 0.5, severity 1 -> 2.5 / 4
        Assert.Equal(0.625, summary.OverallFieldAccuracy, 3);
        Assert.Equal(150, summary.TotalTokens);
    }

    [Fact]
    public void ThresholdCheck_AllPass()
    {
        var summary = EvalSummary.From(Enumerable.Range(0, 10).Select(i => Case($"c{i}")).ToList());

        var checks = ThresholdCheck.Evaluate(summary, Limits);

        Assert.All(checks, c => Assert.True(c.Passed, c.Name));
        Assert.True(ThresholdCheck.AllPassed(checks));
    }

    [Fact]
    public void ThresholdCheck_ListsEachFailure()
    {
        var cases = Enumerable.Range(0, 10).Select(i => Case($"c{i}", latencyMs: 9000, severity: false)).ToList();

        var failed = ThresholdCheck.Evaluate(EvalSummary.From(cases), Limits).Where(c => !c.Passed).Select(c => c.Name).ToList();

        Assert.Contains("p95 latency", failed);
        Assert.Contains("severity accuracy", failed);
        Assert.DoesNotContain("valid after repair", failed);
    }

    [Fact]
    public void Markdown_ContainsSummaryAndPerCaseRows()
    {
        var cases = new List<CaseResult> { Case("ir-01"), Case("ir-02", valid: false, date: false, location: false, equipment: 0, severity: false) };
        var report = new EvalReport(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero), "groq", "openai/gpt-oss-20b", cases, EvalSummary.From(cases));

        var markdown = report.ToMarkdown(Limits);

        Assert.Contains("openai/gpt-oss-20b", markdown, StringComparison.Ordinal);
        Assert.Contains("| ir-01 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| ir-02 |", markdown, StringComparison.Ordinal);
        Assert.Contains("overall field accuracy", markdown, StringComparison.Ordinal);
        Assert.Contains("FAIL", markdown, StringComparison.Ordinal);
    }
}
