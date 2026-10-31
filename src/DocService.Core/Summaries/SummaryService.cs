using System.Text.Json;
using DocService.Core.Extraction;
using DocService.Core.Llm;
using DocService.Core.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocService.Core.Summaries;

public sealed class SummaryService(
    ILlmClient client,
    IOptions<ProcessingOptions> options,
    CostEstimator costEstimator,
    ILogger<SummaryService> logger)
{
    private const string TaskName = "summarise";
    private const int MaxSummaryLength = 2000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public async Task<ProcessingResult<SummaryResult>> SummariseAsync(
        string text,
        int maxSentences,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (text.Length > settings.MaxInputChars)
        {
            return Reject($"text is {text.Length} characters; the limit is {settings.MaxInputChars}");
        }

        var sentences = SentenceSplitter.Split(text);
        if (sentences.Count > settings.MaxSentences)
        {
            return Reject($"text has {sentences.Count} sentences; the limit is {settings.MaxSentences}");
        }

        Log.InputReceived(logger, TaskName, text.Length, InputFingerprint.Of(text));

        var call = new RepairRequest<SummaryResult>(
            TaskName,
            SummaryPrompts.System(maxSentences),
            SummaryPrompts.Document(sentences),
            SummaryPrompts.Schema,
            settings.SummariseMaxOutputTokens,
            content => Parse(content, sentences, maxSentences),
            (previous, errors) => SummaryPrompts.Repair(sentences, previous, errors));

        return await RepairLoop.RunAsync(
            client,
            logger,
            costEstimator,
            new TokenBudget(settings.TokenBudgetPerRequest),
            call,
            cancellationToken);
    }

    private static ParseOutcome<SummaryResult> Parse(string content, IReadOnlyList<string> sentences, int maxSentences)
    {
        SummaryReply? reply;
        try
        {
            reply = JsonSerializer.Deserialize<SummaryReply>(JsonContent.Normalize(content), SerializerOptions);
        }
        catch (JsonException ex)
        {
            return ParseOutcome<SummaryResult>.Invalid([$"invalid JSON: {ex.Message}"]);
        }

        if (reply is null)
        {
            return ParseOutcome<SummaryResult>.Invalid(["invalid JSON: expected an object, got null"]);
        }

        var errors = new List<string>();
        if (reply.Summary.Length is 0 or > MaxSummaryLength)
        {
            errors.Add($"summary: must be 1-{MaxSummaryLength} characters");
        }

        var indices = reply.SourceIndices;
        if (indices.Count == 0 || indices.Count > maxSentences)
        {
            errors.Add($"sourceIndices: must contain 1-{maxSentences} entries");
        }

        if (indices.Distinct().Count() != indices.Count)
        {
            errors.Add("sourceIndices: entries must be distinct");
        }

        errors.AddRange(indices
            .Where(i => i < 0 || i >= sentences.Count)
            .Select(i => $"sourceIndices: index {i} is out of range 0-{sentences.Count - 1}"));

        if (errors.Count > 0)
        {
            return ParseOutcome<SummaryResult>.Invalid(errors);
        }

        var sources = indices.Order().Select(i => new SourceSentence(i, sentences[i])).ToList();
        return ParseOutcome<SummaryResult>.Valid(new SummaryResult(reply.Summary, sources));
    }

    private static ProcessingResult<SummaryResult> Reject(string reason) =>
        ProcessingResult<SummaryResult>.Failure(ProcessingError.InputTooLarge, 0, RequestUsage.None, [reason]);

    private sealed record SummaryReply(string Summary, IReadOnlyList<int> SourceIndices);
}
