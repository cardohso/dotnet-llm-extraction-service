using DocService.Core.Llm;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocService.Api.Errors;

public sealed partial class LlmExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<LlmExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not LlmException llmException)
        {
            return false;
        }

        var (status, title, suffix) = llmException switch
        {
            LlmProviderException => (StatusCodes.Status502BadGateway, "The language model provider returned an error", "provider-error"),
            LlmTimeoutException => (StatusCodes.Status504GatewayTimeout, "The language model provider timed out", "provider-timeout"),
            _ => (StatusCodes.Status503ServiceUnavailable, "The language model provider is unavailable", "provider-unavailable"),
        };

        if (llmException is LlmRateLimitedException { RetryAfter: { } retryAfter })
        {
            httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        LogProviderFailure(logger, llmException.GetType().Name, llmException.Message);
        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = llmException.Message,
                Type = Problems.Type(suffix),
            },
        });
    }

    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Provider failure {ExceptionType}: {Reason}")]
    private static partial void LogProviderFailure(ILogger logger, string exceptionType, string reason);
}
