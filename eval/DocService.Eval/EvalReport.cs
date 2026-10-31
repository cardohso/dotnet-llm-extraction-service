using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DocService.Eval;

internal sealed record CaseResult(
    string Id,
    string Lang,
    bool Valid,
    int Attempts,
    long LatencyMs,
    int Tokens,
    string? Failure,
    bool DateOk,
    bool LocationOk,
    double EquipmentScore,
    bool SeverityOk);

internal sealed record Thresholds(double ValidAfterRepair, double OverallFieldAccuracy, double Severity, double P95LatencyMs)
{
    public static Thresholds Load(string path) =>
        JsonSerializer.Deserialize<Thresholds>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidDataException($"{path} is not a thresholds file");
}

internal static class Percentile
{
    public static long NearestRank(IReadOnlyList<long> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
        return sorted[Math.Max(rank, 1) - 1];
    }
}

internal sealed record EvalSummary(
    int Total,
    double ValidAfterRepairRate,
    double FirstAttemptValidRate,
    double DateAccuracy,
    double LocationAccuracy,
    double EquipmentF1,
    double SeverityAccuracy,
    double OverallFieldAccuracy,
    long P50LatencyMs,
    long P95LatencyMs,
    int TotalTokens)
{
    /// <summary>Invalid cases carry zero scores, so plain means already count them as misses.</summary>
    public static EvalSummary From(IReadOnlyList<CaseResult> cases)
    {
        var date = cases.Average(c => c.DateOk ? 1.0 : 0.0);
        var location = cases.Average(c => c.LocationOk ? 1.0 : 0.0);
        var equipment = cases.Average(c => c.EquipmentScore);
        var severity = cases.Average(c => c.SeverityOk ? 1.0 : 0.0);
        var latencies = cases.Select(c => c.LatencyMs).ToList();
        return new EvalSummary(
            cases.Count,
            cases.Average(c => c.Valid ? 1.0 : 0.0),
            cases.Average(c => c.Valid && c.Attempts == 1 ? 1.0 : 0.0),
            date,
            location,
            equipment,
            severity,
            (date + location + equipment + severity) / 4,
            Percentile.NearestRank(latencies, 50),
            Percentile.NearestRank(latencies, 95),
            cases.Sum(c => c.Tokens));
    }
}

internal sealed record ThresholdResult(string Name, string Actual, string Limit, bool Passed);

internal static class ThresholdCheck
{
    public static IReadOnlyList<ThresholdResult> Evaluate(EvalSummary summary, Thresholds limits) =>
    [
        AtLeast("valid after repair", summary.ValidAfterRepairRate, limits.ValidAfterRepair),
        AtLeast("overall field accuracy", summary.OverallFieldAccuracy, limits.OverallFieldAccuracy),
        AtLeast("severity accuracy", summary.SeverityAccuracy, limits.Severity),
        new ThresholdResult(
            "p95 latency",
            $"{summary.P95LatencyMs} ms",
            $"≤ {limits.P95LatencyMs.ToString("0", CultureInfo.InvariantCulture)} ms",
            summary.P95LatencyMs <= limits.P95LatencyMs),
    ];

    public static bool AllPassed(IEnumerable<ThresholdResult> checks) => checks.All(c => c.Passed);

    private static ThresholdResult AtLeast(string name, double actual, double limit) =>
        new(name, Percent(actual), $"≥ {Percent(limit)}", actual >= limit);

    public static string Percent(double value) => $"{(value * 100).ToString("0.0", CultureInfo.InvariantCulture)} %";
}

internal sealed record EvalReport(
    DateTimeOffset RunAt,
    string Provider,
    string Model,
    IReadOnlyList<CaseResult> Cases,
    EvalSummary Summary)
{
    public string ToMarkdown(Thresholds limits)
    {
        var checks = ThresholdCheck.Evaluate(Summary, limits);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Evaluation run {RunAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Provider: `{Provider}` · model: `{Model}` · cases: {Summary.Total} · tokens: {Summary.TotalTokens}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Result | Threshold | |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var check in checks)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {check.Name} | {check.Actual} | {check.Limit} | {(check.Passed ? "PASS" : "FAIL")} |");
        }

        sb.AppendLine();
        sb.AppendLine("| Field | Score |");
        sb.AppendLine("|---|---|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| date | {ThresholdCheck.Percent(Summary.DateAccuracy)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| location | {ThresholdCheck.Percent(Summary.LocationAccuracy)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| equipment (F1) | {ThresholdCheck.Percent(Summary.EquipmentF1)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| severity | {ThresholdCheck.Percent(Summary.SeverityAccuracy)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| first-attempt valid | {ThresholdCheck.Percent(Summary.FirstAttemptValidRate)} |");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| latency p50 / p95 | {Summary.P50LatencyMs} ms / {Summary.P95LatencyMs} ms |");
        sb.AppendLine();
        sb.AppendLine("| Case | Lang | Valid | Attempts | Latency | Date | Location | Equipment F1 | Severity |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var c in Cases)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {c.Id} | {c.Lang} | {Mark(c.Valid)} | {c.Attempts} | {c.LatencyMs} ms | {Mark(c.DateOk)} | {Mark(c.LocationOk)} | {c.EquipmentScore.ToString("0.00", CultureInfo.InvariantCulture)} | {Mark(c.SeverityOk)} |");
        }

        var failures = Cases.Where(c => c.Failure is not null).ToList();
        if (failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Failures: " + string.Join(", ", failures.Select(f => $"{f.Id} ({f.Failure})")));
        }

        return sb.ToString();
    }

    private static string Mark(bool ok) => ok ? "✓" : "✗";
}
