using System.Diagnostics;
using DocService.Core.Llm;
using Microsoft.Extensions.Logging;

namespace DocService.Core.Processing;

internal sealed record ParseOutcome<T>(T? Value, IReadOnlyList<string> Errors)
{
    public bool IsValid => Value is not null && Errors.Count == 0;

    public static ParseOutcome<T> Valid(T value) => new(value, []);

    public static ParseOutcome<T> Invalid(IReadOnlyList<string> errors) => new(default, errors);
}

internal sealed record RepairRequest<T>(
    string TaskName,
    string SystemPrompt,
    string UserPrompt,
    LlmJsonSchema ResponseSchema,
    int MaxOutputTokens,
    Func<string, ParseOutcome<T>> Parse,
    Func<string, IReadOnlyList<string>, string> BuildRepairPrompt);

/// <summary>
/// Calls the model, validates the reply and, if it is invalid, makes one repair attempt that shows the
/// model its previous output and the validation errors. Stops early on truncation or an exhausted budget.
/// </summary>
internal static class RepairLoop
{
    private const int MaxAttempts = 2;

    public static async Task<ProcessingResult<T>> RunAsync<T>(
        ILlmClient client,
        ILogger logger,
        CostEstimator costEstimator,
        TokenBudget budget,
        RepairRequest<T> call,
        CancellationToken cancellationToken)
    {
        var user = call.UserPrompt;
        IReadOnlyList<string> errors = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var request = new LlmRequest(call.SystemPrompt, user, call.ResponseSchema, call.MaxOutputTokens);
            var started = Stopwatch.GetTimestamp();
            var response = await client.CompleteAsync(request, cancellationToken);
            budget.Record(response.Usage);

            if (response.FinishReason == "length")
            {
                LogCall(logger, call.TaskName, attempt, response, started, "truncated");
                return Fail(ProcessingError.OutputTruncated, attempt, ["model output was cut off at the output token limit"]);
            }

            var parsed = call.Parse(response.Content);
            LogCall(logger, call.TaskName, attempt, response, started, parsed.IsValid ? "valid" : "invalid");
            if (parsed.IsValid)
            {
                return ProcessingResult<T>.Success(parsed.Value!, attempt, costEstimator.ToRequestUsage(budget.Total));
            }

            errors = parsed.Errors;
            if (attempt == MaxAttempts)
            {
                break;
            }

            user = call.BuildRepairPrompt(response.Content, errors);
            var estimate = TokenBudget.Estimate(call.SystemPrompt) + TokenBudget.Estimate(user) + call.MaxOutputTokens;
            if (!budget.CanAfford(estimate))
            {
                return Fail(ProcessingError.BudgetExhausted, attempt, errors);
            }
        }

        return Fail(ProcessingError.InvalidOutput, MaxAttempts, errors);

        ProcessingResult<T> Fail(ProcessingError error, int attempts, IReadOnlyList<string> reasons) =>
            ProcessingResult<T>.Failure(error, attempts, costEstimator.ToRequestUsage(budget.Total), reasons);
    }

    private static void LogCall(ILogger logger, string task, int attempt, LlmResult response, long started, string outcome) =>
        Log.LlmCallCompleted(
            logger,
            task,
            attempt,
            response.Model,
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            outcome);
}
