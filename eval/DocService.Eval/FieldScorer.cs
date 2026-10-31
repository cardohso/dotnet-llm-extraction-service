using DocService.Core.Extraction.IncidentReports;

namespace DocService.Eval;

internal sealed record CaseScores(bool DateOk, bool LocationOk, double EquipmentScore, bool SeverityOk);

/// <summary>Scoring rules are specified in eval/THRESHOLDS.md.</summary>
internal static class FieldScorer
{
    public static CaseScores Score(ExpectedFields expected, IncidentReport? predicted)
    {
        if (predicted is null)
        {
            return new CaseScores(false, false, 0.0, false);
        }

        var expectedDate = expected.Date is null ? (DateOnly?)null : DateOnly.ParseExact(expected.Date, "yyyy-MM-dd");
        return new CaseScores(
            Date(expectedDate, predicted.Date),
            Location(expected.Location, predicted.Location),
            Equipment(expected.Equipment, predicted.Equipment),
            Enum.Parse<Severity>(expected.Severity, ignoreCase: true) == predicted.Severity);
    }

    public static bool Date(DateOnly? expected, DateOnly? actual) => expected == actual;

    public static bool Location(string? expected, string? actual) =>
        expected is null || actual is null ? expected is null && actual is null : Matches(expected, actual);

    /// <summary>Set F1 with one-to-one matching; exact (normalised) matches are paired before contains matches.</summary>
    public static double Equipment(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0 && actual.Count == 0)
        {
            return 1.0;
        }

        if (expected.Count == 0 || actual.Count == 0)
        {
            return 0.0;
        }

        var unmatchedExpected = expected.Select(Normalize).ToList();
        var unmatchedActual = actual.Select(Normalize).ToList();
        var matched = Pair(unmatchedExpected, unmatchedActual, (e, a) => e == a);
        matched += Pair(unmatchedExpected, unmatchedActual, Matches);

        var precision = (double)matched / actual.Count;
        var recall = (double)matched / expected.Count;
        return matched == 0 ? 0.0 : 2 * precision * recall / (precision + recall);
    }

    private static int Pair(List<string> expected, List<string> actual, Func<string, string, bool> isMatch)
    {
        var pairs = 0;
        for (var i = expected.Count - 1; i >= 0; i--)
        {
            var j = actual.FindIndex(a => isMatch(expected[i], a));
            if (j < 0)
            {
                continue;
            }

            expected.RemoveAt(i);
            actual.RemoveAt(j);
            pairs++;
        }

        return pairs;
    }

    private static bool Matches(string expected, string actual)
    {
        var e = Normalize(expected);
        var a = Normalize(actual);
        return e == a || ContainsWords(e, a) || ContainsWords(a, e);
    }

    private static bool ContainsWords(string haystack, string needle) =>
        needle.Length > 0 && $" {haystack} ".Contains($" {needle} ", StringComparison.Ordinal);

    /// <summary>Lower-cases and reduces text to single-space-separated letter/digit words.</summary>
    private static string Normalize(string text)
    {
        var chars = text.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
