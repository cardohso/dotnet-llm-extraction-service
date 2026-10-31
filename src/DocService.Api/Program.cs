using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocService.Api.Endpoints;
using DocService.Api.Errors;
using DocService.Core;
using DocService.Llm;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDocServiceCore(builder.Configuration);
builder.Services.AddLlmClient(builder.Configuration);

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

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "DocService v1"));
}

app.MapGet("/health/live", () => Results.Ok());
app.MapExtract();
app.MapSummarise();

app.Run();

public partial class Program;
