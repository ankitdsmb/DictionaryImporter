namespace DictionaryImporter.Common;

public static class ExampleTextExtensions
{
    private static readonly Regex RxMultiSpace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex RxGarbageStart =
        new(@"^(s\s|s\s*\w+\)|[^\p{L}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxDictionaryMeta =
        new(@"\b(defn|abbr|abbr\.|syn|syn\.|see|examples?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxOcrJunk =
        new(@"[\(\)\[\]【】]", RegexOptions.Compiled);

    private static readonly Regex RxQuantityPattern =
        new(@"\b(one|two|three|four|five|ten|hundred|thousand|\d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxVerbPattern =
        new(@"\b(is|was|were|are|be|been|being|to|of|with|for|by|in|on)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // -------------------------------
    // NORMALIZATION
    // -------------------------------
    public static string NormalizeExample(this string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        var t = example.Trim();

        // remove ALL quotes
        t = t.Replace("“", "")
            .Replace("”", "")
            .Replace("‘", "")
            .Replace("’", "")
            .Replace("\"", "")
            .Replace("'", "")
            .Replace("`", "");

        // normalize whitespace
        t = RxMultiSpace.Replace(t, " ").Trim();

        // remove trailing punctuation chaos
        t = Regex.Replace(t, @"\s*[.!?]+$", "").Trim();

        if (t.Length == 0)
            return string.Empty;

        // capitalize
        if (char.IsLower(t[0]))
            t = char.ToUpperInvariant(t[0]) + t[1..];

        return t + ".";
    }

    // -------------------------------
    // VALIDATION
    // -------------------------------
    public static bool IsValidExampleSentence(this string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return false;

        var t = example.Trim();

        if (t.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return false;

        if (t.Length < 6 || t.Length > 400)
            return false;

        // must start with letter
        if (!char.IsLetter(t[0]))
            return false;

        // reject OCR garbage
        if (RxGarbageStart.IsMatch(t))
            return false;

        // reject dictionary meta
        if (RxDictionaryMeta.IsMatch(t))
            return false;

        // reject control junk
        if (RxOcrJunk.IsMatch(t))
            return false;

        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3)
            return false;

        // must contain verb OR quantity pattern
        if (!RxVerbPattern.IsMatch(t) && !RxQuantityPattern.IsMatch(t))
            return false;

        return true;
    }
}