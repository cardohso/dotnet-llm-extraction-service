using System.Diagnostics;
using DocService.Core.Extraction;
using DocService.Core.Extraction.IncidentReports;
using DocService.Core.Llm;

namespace DocService.Eval;

internal static class EvalRunner
{
    private const int MaxHarnessAttempts = 3;
    private static readonly TimeSpan MaxRateLimitWait = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Runs one golden case. A provider 429 that survives the transport retries is waited out and retried
    /// (free tiers rate-limit by minute); other provider failures are recorded as an invalid case.
    /// </summary>
    public static async Task<CaseResult> RunCaseAsync(
        ExtractionService service,
        GoldenCase golden,
        TimeSpan fallbackRateLimitWait,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var result = await service.ExtractAsync(IncidentReportSchema.SchemaName, golden.Text, cancellationToken);
                var scores = FieldScorer.Score(golden.Expected, result.Value as IncidentReport);
                return new CaseResult(
                    golden.Id,
                    golden.Lang,
                    result.IsSuccess,
                    result.Attempts,
                    ElapsedMs(started),
                    result.Usage.InputTokens + result.Usage.OutputTokens,
                    result.Error?.ToString(),
                    scores.DateOk,
                    scores.LocationOk,
                    scores.EquipmentScore,
                    scores.SeverityOk);
            }
            catch (LlmRateLimitedException ex) when (attempt < MaxHarnessAttempts)
            {
                var wait = ex.RetryAfter ?? fallbackRateLimitWait;
                await Task.Delay(wait > MaxRateLimitWait ? MaxRateLimitWait : wait, cancellationToken);
            }
            catch (LlmException ex)
            {
                return new CaseResult(golden.Id, golden.Lang, false, 0, ElapsedMs(started), 0, ex.GetType().Name, false, false, 0.0, false);
            }
        }
    }

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
