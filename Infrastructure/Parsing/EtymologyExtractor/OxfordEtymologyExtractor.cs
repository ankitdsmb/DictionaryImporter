namespace DictionaryImporter.Infrastructure.Parsing.EtymologyExtractor;

public sealed class OxfordEtymologyExtractor : IEtymologyExtractor
{
    private static readonly Regex EtymologyMarkerRegex =
        new(@"【语源】\s*(?<etymology>.+)", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> LanguageMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "Old English", "ang" },
            { "Middle English", "enm" },
            { "Latin", "la" },
            { "Greek", "el" },
            { "French", "fr" },
            { "German", "de" },
            { "Italian", "it" },
            { "Spanish", "es" },
            { "Dutch", "nl" },
            { "Old Norse", "non" },
            { "Arabic", "ar" },
            { "Hebrew", "he" },
            { "Sanskrit", "sa" }
        };

    private readonly ILogger<OxfordEtymologyExtractor> _logger;

    public OxfordEtymologyExtractor(ILogger<OxfordEtymologyExtractor> logger)
    {
        _logger = logger;
    }

    public string SourceCode => "ENG_OXFORD";

    public EtymologyExtractionResult Extract(
        string headword,
        string definition,
        string? rawDefinition = null)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return new EtymologyExtractionResult
            {
                EtymologyText = null,
                LanguageCode = null,
                CleanedDefinition = definition,
                DetectionMethod = "NoDefinition",
                SourceText = string.Empty
            };

        // Look for 【语源】 marker
        var match = EtymologyMarkerRegex.Match(definition);
        if (!match.Success)
            return new EtymologyExtractionResult
            {
                EtymologyText = null,
                LanguageCode = null,
                CleanedDefinition = definition,
                DetectionMethod = "NoEtymologyMarker",
                SourceText = string.Empty
            };

        var etymologyText = match.Groups["etymology"].Value.Trim();
        var languageCode = ""; // ExtractLanguageCode(etymologyText);

        // Remove etymology from definition for cleaner text
        var cleanedDefinition = EtymologyMarkerRegex.Replace(definition, "").Trim();

        return new EtymologyExtractionResult
        {
            EtymologyText = etymologyText,
            LanguageCode = languageCode,
            CleanedDefinition = cleanedDefinition,
            DetectionMethod = "EtymologyMarker",
            SourceText = match.Value
        };
    }

    public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
    {
        foreach (var kvp in LanguageMappings)
            if (text.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                return (kvp.Key, kvp.Value);

        return (null, null);
    }
}