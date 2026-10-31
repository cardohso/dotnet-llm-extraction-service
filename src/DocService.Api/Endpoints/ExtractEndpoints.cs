using DocService.Api.Errors;
using DocService.Core.Extraction;

namespace DocService.Api.Endpoints;

public static class ExtractEndpoints
{
    public static IEndpointRouteBuilder MapExtract(this IEndpointRouteBuilder app)
    {
        app.MapPost("/extract", async (ExtractRequest request, ExtractionService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ExtractAsync(request.Schema, request.Text, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new ExtractResponse(request.Schema, result.Value!, result.Attempts, result.Usage))
                : Problems.From(result.Error!.Value, result.Errors);
        })
        .WithName("Extract")
        .WithSummary("Extract validated structured data from text")
        .WithDescription("Calls the language model with a JSON schema, validates the result and retries once on invalid output.")
        .Produces<ExtractResponse>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }
}
