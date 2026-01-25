namespace DictionaryImporter.Gateway.Rewriter;

internal static class LuceneTextNormalizer
{
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.Compiled);

    public static string NormalizeForSearch(string text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var t = text.Trim();

        if (t.Length > maxLen)
            t = t.Substring(0, maxLen);

        t = MultiWhitespace.Replace(t, " ");

        return t;
    }

    public static string Preview(string text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var t = text.Trim();
        if (t.Length <= maxLen) return t;

        return t.Substring(0, maxLen).TrimEnd() + "...";
    }

    public static string SafeKeyword(string text, int maxLen)
    {
        var t = NormalizeForSearch(text, maxLen);
        return t;
    }

    public static string HashOrEmpty(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return string.Empty;
        return hash.Trim();
    }

    public static string ModeToString(LuceneSuggestionMode mode)
    {
        return mode switch
        {
            LuceneSuggestionMode.Definition => "Definition",
            LuceneSuggestionMode.MeaningTitle => "MeaningTitle",
            LuceneSuggestionMode.Example => "Example",
            _ => "Unknown"
        };
    }
}