namespace DocService.Core.Processing;

public enum ProcessingError
{
    UnknownSchema,
    InputTooLarge,
    InvalidOutput,
    BudgetExhausted,
    OutputTruncated,
}

public sealed record RequestUsage(int InputTokens, int OutputTokens, decimal EstimatedCostUsd)
{
    public static RequestUsage None { get; } = new(0, 0, 0m);
}

public sealed record ProcessingResult<T>(
    T? Value,
    int Attempts,
    RequestUsage Usage,
    ProcessingError? Error,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Error is null;

    public static ProcessingResult<T> Success(T value, int attempts, RequestUsage usage) =>
        new(value, attempts, usage, null, []);

    public static ProcessingResult<T> Failure(
        ProcessingError error,
        int attempts,
        RequestUsage usage,
        IReadOnlyList<string>? errors = null) =>
        new(default, attempts, usage, error, errors ?? []);
}
