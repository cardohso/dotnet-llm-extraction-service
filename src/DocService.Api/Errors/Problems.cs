using DocService.Core.Processing;

namespace DocService.Api.Errors;

public static class Problems
{
    private const string TypePrefix = "urn:docservice:problem:";

    public static string Type(string suffix) => TypePrefix + suffix;

    public static IResult From(ProcessingError error, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? errors[0] : null;
        return error switch
        {
            ProcessingError.UnknownSchema => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown schema",
                detail: detail,
                type: Type("validation")),
            ProcessingError.InputTooLarge => Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Input too large",
                detail: detail,
                type: Type("input-too-large")),
            _ => Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The model did not return a valid result",
                detail: detail,
                type: Type("llm-output-invalid"),
                extensions: new Dictionary<string, object?>
                {
                    ["reason"] = Reason(error),
                    ["errors"] = errors,
                }),
        };
    }

    private static string Reason(ProcessingError error) => error switch
    {
        ProcessingError.OutputTruncated => "output-truncated",
        ProcessingError.BudgetExhausted => "budget-exhausted",
        _ => "invalid-output",
    };
}
