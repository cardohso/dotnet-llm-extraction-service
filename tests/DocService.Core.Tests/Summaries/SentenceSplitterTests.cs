using DocService.Core.Summaries;

namespace DocService.Core.Tests.Summaries;

public class SentenceSplitterTests
{
    [Theory]
    [InlineData("A. B? C!", 3)]
    [InlineData("See Dr. Silva. Done.", 2)]
    [InlineData("Sem pontuação", 1)]
    [InlineData("Line one\n\nLine two", 2)]
    [InlineData("", 0)]
    [InlineData("   \n  ", 0)]
    public void Split_CountsSentences(string text, int expected) =>
        Assert.Equal(expected, SentenceSplitter.Split(text).Count);

    [Fact]
    public void Split_TrimsAndKeepsOriginalText()
    {
        var sentences = SentenceSplitter.Split("  First one.  Second one!  ");

        Assert.Equal(["First one.", "Second one!"], sentences);
    }

    [Fact]
    public void Split_DoesNotBreakOnAbbreviationEgOrNumbers()
    {
        var sentences = SentenceSplitter.Split("Use tools, e.g. a wrench. Cost was 3.5 euros.");

        Assert.Equal(2, sentences.Count);
    }
}
