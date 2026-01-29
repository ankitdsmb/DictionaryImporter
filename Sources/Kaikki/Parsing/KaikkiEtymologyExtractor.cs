using System.Text.Json;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.Kaikki.Parsing;

internal class KaikkiEtymologyExtractor(ILogger<KaikkiEtymologyExtractor> logger) : IEtymologyExtractor
{
    public string SourceCode => "KAIKKI";

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
            if (!ParsingHelperKaikki.TryParseEnglishRoot(rawDefinition, out _))
            {
                return new EtymologyExtractionResult
                {
                    CleanedDefinition = definition,
                    DetectionMethod = "NotEnglishKaikkiEntry",
                    SourceText = string.Empty
                };
            }

            var etymology = ParsingHelperKaikki.ExtractEtymology(rawDefinition);

            if (!string.IsNullOrWhiteSpace(etymology))
            {
                return new EtymologyExtractionResult
                {
                    EtymologyText = ParsingHelperKaikki.CleanEtymologyText(etymology),
                    LanguageCode = ParsingHelperKaikki.DetectLanguageFromEtymology(etymology),
                    CleanedDefinition = definition,
                    DetectionMethod = "KaikkiStructuredEtymology",
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
        catch (Newtonsoft.Json.JsonException ex)
        {
            logger.LogDebug(ex, "Failed to parse Kaikki JSON for etymology | Word={Word}", headword);

            return new EtymologyExtractionResult
            {
                CleanedDefinition = definition,
                DetectionMethod = "InvalidJson",
                SourceText = string.Empty
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unexpected error extracting Kaikki etymology | Word={Word}", headword);

            return new EtymologyExtractionResult
            {
                CleanedDefinition = definition,
                DetectionMethod = "ExtractorError",
                SourceText = string.Empty
            };
        }
    }
}