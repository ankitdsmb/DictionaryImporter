namespace DictionaryImporter.Core.PreProcessing;

internal static class IpaAutoStressNormalizer
{
    // Detect primary or secondary stress
    private static readonly Regex StressRegex =
        new(@"[ˈˌ]", RegexOptions.Compiled);

    // IPA vowel nuclei (broad but safe)
    private static readonly Regex VowelRegex =
        new(@"[ɑæɐəɛɪiɔʊuʌeɜ]", RegexOptions.Compiled);

    /// <summary>
    ///     Injects primary stress (ˈ) if IPA has multiple syllables
    ///     and no existing stress markers.
    /// </summary>
    public static string Normalize(string ipaWithSlashes)
    {
        if (string.IsNullOrWhiteSpace(ipaWithSlashes))
            return ipaWithSlashes;

        // Strip slashes
        var core = ipaWithSlashes.Trim('/');

        // Already stressed → leave unchanged
        if (StressRegex.IsMatch(core))
            return ipaWithSlashes;

        // Count vowel nuclei (syllable heuristic)
        var vowelCount = VowelRegex.Matches(core).Count;
        if (vowelCount < 2)
            return ipaWithSlashes;

        // Inject primary stress at beginning
        var stressed = "ˈ" + core;

        return $"/{stressed}/";
    }
}