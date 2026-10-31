using System.Text.RegularExpressions;

namespace DocService.Core.Summaries;

public static partial class SentenceSplitter
{
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "e.g", "i.e", "Mr", "Mrs", "Dr", "St", "No", "nº", "Sr", "Sra",
    };

    /// <summary>Splits on blank lines and on . ! ? followed by whitespace, except after known abbreviations.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        var sentences = new List<string>();
        foreach (var block in BlankLine().Split(text))
        {
            var start = 0;
            for (var i = 0; i < block.Length; i++)
            {
                if (block[i] is not ('.' or '!' or '?'))
                {
                    continue;
                }

                var endsSentence = i + 1 == block.Length || char.IsWhiteSpace(block[i + 1]);
                if (!endsSentence || (block[i] == '.' && IsAbbreviation(block, start, i)))
                {
                    continue;
                }

                Add(sentences, block[start..(i + 1)]);
                start = i + 1;
            }

            Add(sentences, block[start..]);
        }

        return sentences;
    }

    private static bool IsAbbreviation(string block, int sentenceStart, int dotIndex)
    {
        var wordStart = dotIndex;
        while (wordStart > sentenceStart && !char.IsWhiteSpace(block[wordStart - 1]))
        {
            wordStart--;
        }

        return Abbreviations.Contains(block[wordStart..dotIndex]);
    }

    private static void Add(List<string> sentences, string candidate)
    {
        var trimmed = candidate.Trim();
        if (trimmed.Length > 0)
        {
            sentences.Add(trimmed);
        }
    }

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex BlankLine();
}
