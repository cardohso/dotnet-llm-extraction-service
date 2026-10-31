using DocService.Api.Errors;
using DocService.Core.Summaries;

namespace DocService.Api.Endpoints;

public static class SummariseEndpoints
{
    public static IEndpointRouteBuilder MapSummarise(this IEndpointRouteBuilder app)
    {
        app.MapPost("/summarise", async (SummariseRequest request, SummaryService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SummariseAsync(request.Text, request.MaxSentences, cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new SummariseResponse(result.Value!.Summary, result.Value.Sources, result.Attempts, result.Usage))
                : Problems.From(result.Error!.Value, result.Errors);
        })
        .WithName("Summarise")
        .WithSummary("Summarise text with the source sentences it is based on")
        .WithDescription("Sources are verbatim sentences from the input, selected by index and checked by the server.")
        .Produces<SummariseResponse>()
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
