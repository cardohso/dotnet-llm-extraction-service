using System.ComponentModel.DataAnnotations;
using DocService.Core.Processing;
using DocService.Core.Summaries;

namespace DocService.Api.Endpoints;

/// <summary>Rejects null, empty and whitespace-only strings.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBlankAttribute : ValidationAttribute
{
    public NotBlankAttribute()
        : base("The {0} field must not be blank.")
    {
    }

    public override bool IsValid(object? value) => value is string { } s && !string.IsNullOrWhiteSpace(s);
}

public sealed record ExtractRequest(
    [property: NotBlank] string Schema,
    [property: NotBlank] string Text);

public sealed record ExtractResponse(string Schema, object Data, int Attempts, RequestUsage Usage);

public sealed record SummariseRequest(
    [property: NotBlank] string Text,
    [property: Range(1, 10)] int MaxSentences = 3);

public sealed record SummariseResponse(
    string Summary,
    IReadOnlyList<SourceSentence> Sources,
    int Attempts,
    RequestUsage Usage);
