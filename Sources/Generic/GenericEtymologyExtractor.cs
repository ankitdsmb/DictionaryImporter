using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.Generic;

public sealed class GenericEtymologyExtractor(ILogger<GenericEtymologyExtractor> logger) : IEtymologyExtractor
{
    private static readonly Regex GenericEtymRegex =
        new(@"(?:Etymology|Origin):?\s*(?<etym>[^\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> GenericLanguageMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "latin", "la" },
            { "greek", "el" },
            { "french", "fr" },
            { "german", "de" },
            { "old english", "ang" },
            { "middle english", "enm" }
        };

    private readonly ILogger<GenericEtymologyExtractor> _logger = logger;

    public string SourceCode => "*";

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

        var etymMatch = GenericEtymRegex.Match(definition);
        if (etymMatch.Success)
        {
            var etymologyText = etymMatch.Groups["etym"].Value.Trim();

            string? languageCode = null;
            foreach (var mapping in GenericLanguageMappings)
                if (etymologyText.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    languageCode = mapping.Value;
                    break;
                }

            var cleanedDefinition = definition
                .Remove(etymMatch.Index, etymMatch.Length)
                .Trim();

            return new EtymologyExtractionResult
            {
                EtymologyText = etymologyText,
                LanguageCode = languageCode,
                CleanedDefinition = cleanedDefinition,
                DetectionMethod = "GenericEtymologyMarker",
                SourceText = etymMatch.Value
            };
        }

        return new EtymologyExtractionResult
        {
            EtymologyText = null,
            LanguageCode = null,
            CleanedDefinition = definition,
            DetectionMethod = "None",
            SourceText = string.Empty
        };
    }

    public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);

        var match = GenericEtymRegex.Match(text);
        if (match.Success)
        {
            var etymology = match.Groups["etym"].Value.Trim();
            string? languageCode = null;

            foreach (var mapping in GenericLanguageMappings)
                if (etymology.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    languageCode = mapping.Value;
                    break;
                }

            return (etymology, languageCode);
        }

        return (null, null);
    }
}