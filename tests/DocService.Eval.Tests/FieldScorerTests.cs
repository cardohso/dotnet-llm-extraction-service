using DocService.Core.Extraction.IncidentReports;
using DocService.Eval;

namespace DocService.Eval.Tests;

public class FieldScorerTests
{
    [Theory]
    [InlineData("York", "York station", true)]
    [InlineData("York station", "york", true)]
    [InlineData("  Doncaster   station, platform 4 ", "doncaster station platform 4", true)]
    [InlineData("Coimbra-B", "Coimbra B", true)]
    [InlineData("Porto", "Lisboa", false)]
    [InlineData("Port", "Porto Campanhã", false)]
    [InlineData("Faro", null, false)]
    [InlineData(null, null, true)]
    [InlineData(null, "Faro", false)]
    public void Location_EqualOrContainsOnWordBoundaries(string? expected, string? actual, bool match) =>
        Assert.Equal(match, FieldScorer.Location(expected, actual));

    [Fact]
    public void Date_NullVsValue_False()
    {
        Assert.False(FieldScorer.Date(null, new DateOnly(2026, 1, 1)));
        Assert.False(FieldScorer.Date(new DateOnly(2026, 1, 1), null));
        Assert.True(FieldScorer.Date(null, null));
        Assert.True(FieldScorer.Date(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Equipment_F1_PartialMatch() =>
        Assert.Equal(0.5, FieldScorer.Equipment(["a", "b"], ["a", "c"]), 3);

    [Fact]
    public void Equipment_BothEmpty_IsOne() =>
        Assert.Equal(1.0, FieldScorer.Equipment([], []));

    [Theory]
    [InlineData(new string[0], new[] { "x" })]
    [InlineData(new[] { "x" }, new string[0])]
    public void Equipment_OneSideEmpty_IsZero(string[] expected, string[] actual) =>
        Assert.Equal(0.0, FieldScorer.Equipment(expected, actual));

    [Fact]
    public void Equipment_ContainsMatch_IsOneToOne()
    {
        // "signal" can only match one predicted item; the second prediction counts as a false positive.
        var score = FieldScorer.Equipment(["signal CE112"], ["signal CE112", "signal"]);

        Assert.Equal(2.0 / 3.0, score, 3);
    }

    [Fact]
    public void Equipment_ExactMatchesTakePrecedence()
    {
        var score = FieldScorer.Equipment(["signal", "signal CE112"], ["signal CE112", "signal"]);

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Score_NullPrediction_ScoresZeroEverywhere()
    {
        var scores = FieldScorer.Score(new ExpectedFields(null, null, [], "unknown"), null);

        Assert.False(scores.DateOk);
        Assert.False(scores.LocationOk);
        Assert.Equal(0.0, scores.EquipmentScore);
        Assert.False(scores.SeverityOk);
    }

    [Fact]
    public void Score_PerfectPrediction()
    {
        var expected = new ExpectedFields("2026-03-03", "Entroncamento depot", ["points machine P12"], "medium");
        var predicted = new IncidentReport(new DateOnly(2026, 3, 3), "Entroncamento depot", ["points machine P12"], Severity.Medium, "x");

        var scores = FieldScorer.Score(expected, predicted);

        Assert.True(scores.DateOk && scores.LocationOk && scores.SeverityOk);
        Assert.Equal(1.0, scores.EquipmentScore);
    }
}
