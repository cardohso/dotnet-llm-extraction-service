using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocService.Api.Errors;

/// <summary>Turns request-binding failures (malformed JSON, oversized body) into 4xx problems instead of 500.</summary>
public sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        var tooLarge = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge;
        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = tooLarge ? "Input too large" : "Invalid request",
                Detail = tooLarge
                    ? "The request body is larger than the server accepts."
                    : "The request body is missing, is not valid JSON, or does not match the expected shape.",
                Type = Problems.Type(tooLarge ? "input-too-large" : "validation"),
            },
        });
    }
}
