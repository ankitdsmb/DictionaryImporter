using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.Gutenberg.Extractor;

internal sealed class GutenbergEtymologyExtractor(ILogger<GutenbergEtymologyExtractor> logger) : IEtymologyExtractor
{
    public string SourceCode => "GUT_WEBSTER";

    public EtymologyExtractionResult Extract(string headword, string definition, string? rawDefinition = null)
    {
        if (string.IsNullOrWhiteSpace(rawDefinition))
        {
            return new EtymologyExtractionResult
            {
                CleanedDefinition = definition,
                DetectionMethod = "MissingRawDefinition",
                SourceText = string.Empty
            };
        }

        try
        {
            var etymology = ParsingHelperGutenberg.ExtractEtymology(rawDefinition);

            if (!string.IsNullOrWhiteSpace(etymology))
            {
                return new EtymologyExtractionResult
                {
                    EtymologyText = ParsingHelperKaikki.CleanEtymologyText(etymology),
                    LanguageCode = ParsingHelperKaikki.DetectLanguageFromEtymology(etymology),
                    CleanedDefinition = definition,
                    DetectionMethod = "GutenbergEtymologyMarker",
                    SourceText = etymology
                };
            }

            return new EtymologyExtractionResult
            {
                CleanedDefinition = definition,
                DetectionMethod = "NoEtymologyFound",
                SourceText = string.Empty
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error extracting Gutenberg etymology | Word={Word}", headword);

            return new EtymologyExtractionResult
            {
                CleanedDefinition = definition,
                DetectionMethod = "ExtractorError",
                SourceText = string.Empty
            };
        }
    }
}