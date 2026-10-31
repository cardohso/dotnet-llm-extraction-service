using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using DocService.Api.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DocService.Api.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Burst size per client IP.</summary>
    [Range(1, 10_000)]
    public int TokenLimit { get; set; } = 5;

    [Range(1, 10_000)]
    public int TokensPerPeriod { get; set; } = 1;

    [Range(1, 3600)]
    public int ReplenishmentSeconds { get; set; } = 3;

    /// <summary>Requests being processed at once, across all clients.</summary>
    [Range(1, 10_000)]
    public int ConcurrencyLimit { get; set; } = 4;

    [Range(0, 10_000)]
    public int QueueLimit { get; set; } = 10;
}

public static class RateLimitingSetup
{
    private static readonly string[] ExemptPrefixes = ["/health", "/openapi", "/swagger"];

    public static IServiceCollection AddDocServiceRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((limiter, settings) =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    PerClient(settings.Value),
                    Concurrency(settings.Value));
                limiter.OnRejected = WriteProblem;
            });
        return services;
    }

    private static bool IsExempt(HttpContext context) =>
        ExemptPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static PartitionedRateLimiter<HttpContext> PerClient(RateLimitingOptions settings) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsExempt(context)
                ? RateLimitPartition.GetNoLimiter("exempt")
                : RateLimitPartition.GetTokenBucketLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = settings.TokenLimit,
                        TokensPerPeriod = settings.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(settings.ReplenishmentSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

    private static PartitionedRateLimiter<HttpContext> Concurrency(RateLimitingOptions settings) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsExempt(context)
                ? RateLimitPartition.GetNoLimiter("exempt")
                : RateLimitPartition.GetConcurrencyLimiter(
                    "llm",
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = settings.ConcurrencyLimit,
                        QueueLimit = settings.QueueLimit,
                    }));

    private static async ValueTask WriteProblem(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? delay : TimeSpan.FromSeconds(1);
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString("0", CultureInfo.InvariantCulture);

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Rate limit exceeded. Retry after the delay in the Retry-After header.",
                Type = Problems.Type("rate-limited"),
            },
        });
    }
}
