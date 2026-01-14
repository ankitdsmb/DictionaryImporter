namespace DictionaryImporter.Core.PreProcessing;

internal static class IpaAutoStressNormalizer
{
    private static readonly Regex StressRegex =
        new(@"[ˈˌ]", RegexOptions.Compiled);

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

        var core = ipaWithSlashes.Trim('/');

        if (StressRegex.IsMatch(core))
            return ipaWithSlashes;

        var vowelCount = VowelRegex.Matches(core).Count;
        if (vowelCount < 2)
            return ipaWithSlashes;

        var stressed = "ˈ" + core;

        return $"/{stressed}/";
    }
}