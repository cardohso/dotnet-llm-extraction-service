using DocService.Core.Llm;
using DocService.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocService.Core.Extraction;

public sealed class ExtractionService(
    ILlmClient client,
    ExtractionSchemaRegistry registry,
    IOptions<ProcessingOptions> options,
    CostEstimator costEstimator,
    ILogger<ExtractionService> logger)
{
    private const string TaskName = "extract";

    public async Task<ProcessingResult<object>> ExtractAsync(
        string schemaName,
        string text,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!registry.TryGet(schemaName, out var schema))
        {
            return Reject(ProcessingError.UnknownSchema,
                $"unknown schema '{schemaName}'; available: {string.Join(", ", registry.Names)}");
        }

        if (text.Length > settings.MaxInputChars)
        {
            return Reject(ProcessingError.InputTooLarge,
                $"text is {text.Length} characters; the limit is {settings.MaxInputChars}");
        }

        Log.InputReceived(logger, TaskName, text.Length, InputFingerprint.Of(text));

        var call = new RepairRequest<object>(
            TaskName,
            ExtractionPrompts.System(schema),
            text,
            schema.ResponseSchema,
            settings.ExtractMaxOutputTokens,
            content =>
            {
                var parsed = schema.Parse(content);
                return parsed.IsValid ? ParseOutcome<object>.Valid(parsed.Data!) : ParseOutcome<object>.Invalid(parsed.Errors);
            },
            (previous, errors) => ExtractionPrompts.Repair(text, previous, errors));

        return await RepairLoop.RunAsync(
            client,
            logger,
            costEstimator,
            new TokenBudget(settings.TokenBudgetPerRequest),
            call,
            cancellationToken);
    }

    private static ProcessingResult<object> Reject(ProcessingError error, string reason) =>
        ProcessingResult<object>.Failure(error, 0, RequestUsage.None, [reason]);
}
