namespace DocService.Core.Summaries;

public sealed record SourceSentence(int Index, string Text);

public sealed record SummaryResult(string Summary, IReadOnlyList<SourceSentence> Sources);
