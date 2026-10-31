using Microsoft.Extensions.Logging;

namespace DocService.Core.Processing;

internal static partial class Log
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Received {Task} input: {Length} chars, sha256 {HashPrefix}")]
    public static partial void InputReceived(ILogger logger, string task, int length, string hashPrefix);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "LLM call {Task} attempt {Attempt} model {Model}: {InputTokens} in / {OutputTokens} out, {LatencyMs} ms, {Outcome}")]
    public static partial void LlmCallCompleted(
        ILogger logger,
        string task,
        int attempt,
        string model,
        int inputTokens,
        int outputTokens,
        long latencyMs,
        string outcome);
}
