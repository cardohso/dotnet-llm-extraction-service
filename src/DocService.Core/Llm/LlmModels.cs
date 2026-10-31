using System.Text.Json.Nodes;

namespace DocService.Core.Llm;

public sealed record LlmJsonSchema(string Name, JsonObject Schema);

public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    LlmJsonSchema? ResponseSchema,
    int MaxOutputTokens,
    double Temperature = 0);

public sealed record LlmUsage(int InputTokens, int OutputTokens)
{
    public static LlmUsage Zero { get; } = new(0, 0);

    public static LlmUsage operator +(LlmUsage a, LlmUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens);
}

public sealed record LlmResult(string Content, LlmUsage Usage, string Model, string FinishReason);
