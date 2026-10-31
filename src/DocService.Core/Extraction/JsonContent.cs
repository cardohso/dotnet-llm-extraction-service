namespace DocService.Core.Extraction;

public static class JsonContent
{
    /// <summary>
    /// Returns the outermost JSON object in a model reply, dropping Markdown fences or prose around it
    /// (single- or multi-line). Text without an object is returned trimmed, so parsing reports the error.
    /// </summary>
    public static string Normalize(string content)
    {
        var text = content.Trim();
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }
}
