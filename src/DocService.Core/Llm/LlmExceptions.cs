namespace DocService.Core.Llm;

public abstract class LlmException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class LlmRateLimitedException(string message, TimeSpan? retryAfter = null)
    : LlmException(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class LlmUnavailableException(string message, Exception? innerException = null)
    : LlmException(message, innerException);

public sealed class LlmTimeoutException(string message, Exception? innerException = null)
    : LlmException(message, innerException);

public sealed class LlmProviderException(string message, int? statusCode = null, Exception? innerException = null)
    : LlmException(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
}
