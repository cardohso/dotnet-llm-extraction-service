namespace DocService.Core.Extraction;

public static class JsonContent
{
    private const string Fence = "```";

    /// <summary>Trims whitespace and removes a surrounding Markdown code fence, if present.</summary>
    public static string Normalize(string content)
    {
        var text = content.Trim();
        if (!text.StartsWith(Fence, StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n', StringComparison.Ordinal);
        text = firstNewline < 0 ? string.Empty : text[(firstNewline + 1)..];
        text = text.TrimEnd();
        if (text.EndsWith(Fence, StringComparison.Ordinal))
        {
            text = text[..^Fence.Length];
        }

        return text.Trim();
    }
}
