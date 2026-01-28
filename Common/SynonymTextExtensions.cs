namespace DictionaryImporter.Common;

public static class SynonymTextExtensions
{
    private static readonly Regex RxMultiSpace =
        new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex RxMorphologyNoise =
        new(@"\b(pl|sing|vb|vb\.n|imp|p\.p|p\.pr|comp|superl)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxDictionaryMeta =
        new(@"\b(syn|synonyms?|see|see also|etc|viz|cf)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxAuthorNoise =
        new(@"\b(shak|chaucer|milton|dryden|lowell|tennyson|wordsworth|bible)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RxInvalidChars =
        new(@"[^A-Za-z\s\-]", RegexOptions.Compiled);

    // -------------------------
    // NORMALIZATION
    // -------------------------
    public static string NormalizeSynonym(this string synonym)
    {
        if (string.IsNullOrWhiteSpace(synonym))
            return string.Empty;

        var t = synonym.Trim();

        t = t.Replace("’", "'")
            .Replace("`", "")
            .Replace("\"", "")
            .Replace(".", "")
            .Replace(",", "")
            .Replace(";", "")
            .Replace(":", "");

        t = RxMultiSpace.Replace(t, " ").Trim();

        return t;
    }

    // -------------------------
    // VALIDATION
    // -------------------------
    public static bool IsValidSynonym(
        this string synonym,
        string headword)
    {
        if (string.IsNullOrWhiteSpace(synonym))
            return false;

        var t = synonym.Trim();

        if (t.Length < 2 || t.Length > 40)
            return false;

        if (RxDictionaryMeta.IsMatch(t))
            return false;

        if (RxMorphologyNoise.IsMatch(t))
            return false;

        if (RxAuthorNoise.IsMatch(t))
            return false;

        if (RxInvalidChars.IsMatch(t))
            return false;

        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 3)
            return false;

        if (!t.Any(char.IsLetter))
            return false;

        // reject self-synonym (normalized)
        if (!string.IsNullOrWhiteSpace(headword))
        {
            var a = NormalizeForCompare(headword);
            var b = NormalizeForCompare(t);
            if (a == b)
                return false;
        }

        return true;
    }

    private static string NormalizeForCompare(string text)
    {
        return text
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("'", "");
    }
}