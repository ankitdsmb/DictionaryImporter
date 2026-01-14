namespace DictionaryImporter.Core.Linguistics;

/// <summary>
///     Extracts orthographic (spelling-based) syllables.
///     Example:
///     dominion → do | min | ion
///     Rules are conservative and reader-oriented.
///     No IPA. No stress. No guessing.
/// </summary>
public static class OrthographicSyllableExtractor
{
    private static readonly Regex VowelRegex =
        new(@"[aeiouyAEIOUY]", RegexOptions.Compiled);

    /// <summary>
    ///     Extracts orthographic syllables from a word.
    /// </summary>
    public static IReadOnlyList<string> Extract(string word)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(word))
            return result;

        word = word.Trim();

        var last = 0;

        for (var i = 1; i < word.Length - 1; i++)
            if (VowelRegex.IsMatch(word[i - 1].ToString()) &&
                !VowelRegex.IsMatch(word[i].ToString()) &&
                VowelRegex.IsMatch(word[i + 1].ToString()))
            {
                result.Add(word.Substring(last, i - last));
                last = i;
            }

        result.Add(word.Substring(last));
        return result;
    }
}