using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocService.Api.Endpoints;
using DocService.Api.Errors;
using DocService.Api.Health;
using DocService.Api.RateLimiting;
using DocService.Core;
using DocService.Llm;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.Services.AddDocServiceCore(builder.Configuration);
builder.Services.AddLlmClient(builder.Configuration);
builder.Services.AddDocServiceRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<ProviderCircuitHealthCheck>("llm-circuit", tags: ["ready"]);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddExceptionHandler<LlmExceptionHandler>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var problem = context.ProblemDetails;
    problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    if (problem.Type is null or { Length: 0 } || problem.Type.StartsWith("https://tools.ietf.org/", StringComparison.Ordinal))
    {
        problem.Type = Problems.Type(problem.Status switch
        {
            StatusCodes.Status400BadRequest => "validation",
            StatusCodes.Status404NotFound => "not-found",
            _ => "internal-error",
        });
    }
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DocService v1"));
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapExtract();
app.MapSummarise();

try
{
    app.Run();
}
catch (OptionsValidationException ex)
{
    // Fail with a readable message and a clean exit code instead of an unhandled-exception crash.
    app.Logger.LogCritical("Invalid configuration: {Failures}", string.Join(" ", ex.Failures));
    return 1;
}

return 0;

public partial class Program;
