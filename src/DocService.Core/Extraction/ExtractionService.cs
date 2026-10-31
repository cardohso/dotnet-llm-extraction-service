using System.Diagnostics;
using DocService.Core.Llm;
using DocService.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocService.Core.Extraction;

public sealed class ExtractionService(
    ILlmClient client,
    ExtractionSchemaRegistry registry,
    IOptions<ProcessingOptions> options,
    CostEstimator costEstimator,
    ILogger<ExtractionService> logger)
{
    private const string TaskName = "extract";
    private const int MaxAttempts = 2;

    public async Task<ProcessingResult<object>> ExtractAsync(
        string schemaName,
        string text,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!registry.TryGet(schemaName, out var schema))
        {
            return Fail(ProcessingError.UnknownSchema, 0, LlmUsage.Zero, [
                $"unknown schema '{schemaName}'; available: {string.Join(", ", registry.Names)}",
            ]);
        }

        if (text.Length > settings.MaxInputChars)
        {
            return Fail(ProcessingError.InputTooLarge, 0, LlmUsage.Zero, [
                $"text is {text.Length} characters; the limit is {settings.MaxInputChars}",
            ]);
        }

        Log.InputReceived(logger, TaskName, text.Length, InputFingerprint.Of(text));

        var budget = new TokenBudget(settings.TokenBudgetPerRequest);
        var system = ExtractionPrompts.System(schema);
        var user = text;
        IReadOnlyList<string> errors = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var request = new LlmRequest(system, user, schema.ResponseSchema, settings.ExtractMaxOutputTokens);
            var started = Stopwatch.GetTimestamp();
            var response = await client.CompleteAsync(request, cancellationToken);
            budget.Record(response.Usage);

            if (response.FinishReason == "length")
            {
                LogCall(attempt, response, started, "truncated");
                return Fail(ProcessingError.OutputTruncated, attempt, budget.Total, [
                    "model output was cut off at the output token limit",
                ]);
            }

            var parsed = schema.Parse(response.Content);
            LogCall(attempt, response, started, parsed.IsValid ? "valid" : "invalid");
            if (parsed.IsValid)
            {
                return ProcessingResult<object>.Success(
                    parsed.Data!,
                    attempt,
                    costEstimator.ToRequestUsage(budget.Total));
            }

            errors = parsed.Errors;
            if (attempt == MaxAttempts)
            {
                break;
            }

            user = ExtractionPrompts.Repair(text, response.Content, errors);
            var repairEstimate = TokenBudget.Estimate(system) + TokenBudget.Estimate(user) + settings.ExtractMaxOutputTokens;
            if (!budget.CanAfford(repairEstimate))
            {
                return Fail(ProcessingError.BudgetExhausted, attempt, budget.Total, errors);
            }
        }

        return Fail(ProcessingError.InvalidOutput, MaxAttempts, budget.Total, errors);
    }

    private ProcessingResult<object> Fail(
        ProcessingError error,
        int attempts,
        LlmUsage usage,
        IReadOnlyList<string> errors) =>
        ProcessingResult<object>.Failure(error, attempts, costEstimator.ToRequestUsage(usage), errors);

    private void LogCall(int attempt, LlmResult response, long started, string outcome) =>
        Log.LlmCallCompleted(
            logger,
            TaskName,
            attempt,
            response.Model,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            outcome);
}
