using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.EnglishChinese.Extractor;

public sealed class EnglishChineseEtymologyExtractor(ILogger<EnglishChineseEtymologyExtractor> logger)
    : IEtymologyExtractor
{
    public string SourceCode => "ENG_CHN";

    public EtymologyExtractionResult Extract(string headword, string definition, string? rawDefinition = null)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return new EtymologyExtractionResult
            {
                EtymologyText = null,
                LanguageCode = null,
                CleanedDefinition = definition,
                DetectionMethod = "MissingDefinition",
                SourceText = string.Empty
            };
        }

        try
        {
            // Use helper class to parse the English-Chinese entry
            var parsedData = ParsingHelperEnglishChinese.ParseEngChnEntry(definition);

            // Extract etymology from parsed data
            var etymologyText = parsedData.Etymology;

            if (!string.IsNullOrWhiteSpace(etymologyText))
            {
                return new EtymologyExtractionResult
                {
                    EtymologyText = CleanEtymologyText(etymologyText),
                    CleanedDefinition = GetCleanedDefinition(parsedData),
                    DetectionMethod = "ENG_CHN_ParsingHelper",
                    SourceText = etymologyText
                };
            }

            // Check additional senses for etymology
            if (parsedData.AdditionalSenses != null && parsedData.AdditionalSenses.Count > 0)
            {
                foreach (var sense in parsedData.AdditionalSenses)
                {
                    if (!string.IsNullOrWhiteSpace(sense.Etymology))
                    {
                        return new EtymologyExtractionResult
                        {
                            EtymologyText = CleanEtymologyText(sense.Etymology),
                            CleanedDefinition = GetCleanedDefinition(parsedData),
                            DetectionMethod = "ENG_CHN_ParsingHelper_AdditionalSense",
                            SourceText = sense.Etymology
                        };
                    }
                }
            }

            return new EtymologyExtractionResult
            {
                EtymologyText = null,
                LanguageCode = null,
                CleanedDefinition = GetCleanedDefinition(parsedData),
                DetectionMethod = "NoEtymologyFound",
                SourceText = string.Empty
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error extracting ENG_CHN etymology | Word={Word}", headword);

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

    private static string CleanEtymologyText(string etymology)
    {
        if (string.IsNullOrWhiteSpace(etymology))
            return etymology;

        // Remove brackets and angle brackets if present
        var cleaned = etymology
            .Replace("[", "")
            .Replace("]", "")
            .Replace("<", "")
            .Replace(">", "")
            .Trim();

        // Remove "字面意义：" prefix if present
        if (cleaned.StartsWith("字面意义："))
            cleaned = cleaned["字面意义：".Length..].Trim();

        return cleaned;
    }
    private static string GetCleanedDefinition(EnglishChineseParsedData parsedData)
    {
        // Use the main definition from parsed data
        var cleaned = parsedData.MainDefinition;

        // If main definition is empty, extract Chinese definition from English side
        if (string.IsNullOrWhiteSpace(cleaned) && !string.IsNullOrWhiteSpace(parsedData.EnglishDefinition))
        {
            cleaned = ParsingHelperEnglishChinese.ExtractChineseDefinition(parsedData.EnglishDefinition);
        }

        return cleaned ?? string.Empty;
    }
}