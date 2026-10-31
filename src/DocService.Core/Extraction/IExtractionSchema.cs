using DocService.Core.Llm;

namespace DocService.Core.Extraction;

public interface IExtractionSchema
{
    string Name { get; }

    /// <summary>Task-specific guidance appended to the system prompt.</summary>
    string Instructions { get; }

    LlmJsonSchema ResponseSchema { get; }

    SchemaParseResult Parse(string content);
}

public sealed record SchemaParseResult(object? Data, IReadOnlyList<string> Errors)
{
    public bool IsValid => Data is not null && Errors.Count == 0;

    public static SchemaParseResult Success(object data) => new(data, []);

    public static SchemaParseResult Failure(params string[] errors) => new(null, errors);
}
