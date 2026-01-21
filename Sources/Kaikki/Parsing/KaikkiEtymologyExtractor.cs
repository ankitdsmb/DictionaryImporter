using System.Text.Json;
using DictionaryImporter.Sources.Common.Helper;
using JsonException = Newtonsoft.Json.JsonException;

namespace DictionaryImporter.Sources.Kaikki.Parsing
{
    internal class KaikkiEtymologyExtractor(ILogger<KaikkiEtymologyExtractor> logger) : IEtymologyExtractor
    {
        public string SourceCode => "KAIKKI";

        public EtymologyExtractionResult Extract(string headword, string definition, string? rawDefinition = null)
        {
            if (string.IsNullOrWhiteSpace(rawDefinition))
            {
                return new EtymologyExtractionResult
                {
                    EtymologyText = null,
                    LanguageCode = null,
                    CleanedDefinition = definition,
                    DetectionMethod = "MissingRawDefinition",
                    SourceText = string.Empty
                };
            }

            try
            {
                using var doc = JsonDocument.Parse(rawDefinition);
                var root = doc.RootElement;

                // Skip non-English entries safely (consistent with Kaikki transformer/parser)
                if (!JsonProcessor.IsEnglishEntry(root))
                {
                    return new EtymologyExtractionResult
                    {
                        EtymologyText = null,
                        LanguageCode = null,
                        CleanedDefinition = definition,
                        DetectionMethod = "NotEnglishKaikkiEntry",
                        SourceText = string.Empty
                    };
                }

                var etymology = SourceDataHelper.ExtractEtymology(rawDefinition);

                if (!string.IsNullOrWhiteSpace(etymology))
                {
                    return new EtymologyExtractionResult
                    {
                        EtymologyText = SourceDataHelper.CleanEtymologyText(etymology),
                        LanguageCode = SourceDataHelper.DetectLanguageFromEtymology(etymology),
                        CleanedDefinition = definition,
                        DetectionMethod = "KaikkiStructuredEtymology",
                        SourceText = etymology
                    };
                }

                return new EtymologyExtractionResult
                {
                    EtymologyText = null,
                    LanguageCode = null,
                    CleanedDefinition = definition,
                    DetectionMethod = "NoEtymologyFound",
                    SourceText = string.Empty
                };
            }
            catch (JsonException ex)
            {
                logger.LogDebug(ex, "Failed to parse Kaikki JSON for etymology | Word={Word}", headword);

                return new EtymologyExtractionResult
                {
                    EtymologyText = null,
                    LanguageCode = null,
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
                    EtymologyText = null,
                    LanguageCode = null,
                    CleanedDefinition = definition,
                    DetectionMethod = "ExtractorError",
                    SourceText = string.Empty
                };
            }
        }

        public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
        {
            return (null, null);
        }
    }
}