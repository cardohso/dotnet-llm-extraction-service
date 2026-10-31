using DocService.Core.Llm;

namespace DocService.Core.Processing;

/// <summary>Tracks token usage across the attempts of one request.</summary>
public sealed class TokenBudget(int limit)
{
    public LlmUsage Total { get; private set; } = LlmUsage.Zero;

    public int Used => Total.InputTokens + Total.OutputTokens;

    /// <summary>Rough pre-call estimate: one token per four characters, rounded up.</summary>
    public static int Estimate(string text) => (text.Length + 3) / 4;

    public void Record(LlmUsage usage) => Total += usage;

    public bool CanAfford(int estimatedTokens) => Used + estimatedTokens <= limit;
}
