namespace DocService.Llm;

public enum LlmProvider
{
    OpenAiCompatible,
    Fake,
}

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.OpenAiCompatible;

    public Uri BaseUrl { get; set; } = new("https://api.groq.com/openai/v1/");

    public string Model { get; set; } = "openai/gpt-oss-20b";

    public string? ApiKey { get; set; }

    public string? ReasoningEffort { get; set; } = "low";
}
