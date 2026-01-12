namespace DictionaryImporter.Core.PreProcessing;

internal static class IpaLocaleDetector
{
    // Strong American indicators
    private static readonly Regex AmericanMarkers =
        new(@"[ɹɑɚɝoʊ]", RegexOptions.Compiled);

    // Strong British indicators
    private static readonly Regex BritishMarkers =
        new(@"[ɒəʊː]", RegexOptions.Compiled);

    /// <summary>
    ///     Detects IPA locale using phonetic markers.
    ///     Returns a BCP-47 language tag.
    /// </summary>
    public static string Detect(string ipa)
    {
        if (string.IsNullOrWhiteSpace(ipa))
            return "en";

        var usScore = 0;
        var gbScore = 0;

        // Core markers
        if (AmericanMarkers.IsMatch(ipa))
            usScore++;

        if (BritishMarkers.IsMatch(ipa))
            gbScore++;

        // High-confidence markers
        if (ipa.Contains("ɚ") || ipa.Contains("ɝ"))
            usScore += 2;

        if (ipa.Contains("ː"))
            gbScore += 2;

        if (usScore > gbScore)
            return "en-US";

        if (gbScore > usScore)
            return "en-GB";

        return "en"; // neutral fallback
    }

    /// <summary>
    ///     Optional compatibility mapping for systems using en-UK.
    /// </summary>
    public static string MapToSystemLocale(string detectedLocale)
    {
        return detectedLocale switch
        {
            "en-GB" => "en-UK", // legacy compatibility
            _ => detectedLocale
        };
    }
}