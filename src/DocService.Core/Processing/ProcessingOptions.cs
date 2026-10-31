using System.ComponentModel.DataAnnotations;

namespace DocService.Core.Processing;

public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    [Range(1, 1_000_000)]
    public int MaxInputChars { get; set; } = 20_000;

    [Range(1, 1_000_000)]
    public int TokenBudgetPerRequest { get; set; } = 16_000;

    [Range(1, 100_000)]
    public int ExtractMaxOutputTokens { get; set; } = 800;

    [Range(1, 100_000)]
    public int SummariseMaxOutputTokens { get; set; } = 600;

    [Range(1, 10_000)]
    public int MaxSentences { get; set; } = 200;
}
