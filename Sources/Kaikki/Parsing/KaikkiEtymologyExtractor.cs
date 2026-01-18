using DictionaryImporter.Sources.Kaikki.Helpers;

namespace DictionaryImporter.Infrastructure.Parsing.EtymologyExtractor
{
    internal class KaikkiEtymologyExtractor : IEtymologyExtractor
    {
        private readonly ILogger<KaikkiEtymologyExtractor> _logger;

        public KaikkiEtymologyExtractor(ILogger<KaikkiEtymologyExtractor> logger)
        {
            _logger = logger;
        }

        public string SourceCode => "KAIKKI";

        public EtymologyExtractionResult Extract(
            string headword,
            string definition,
            string? rawDefinition = null)
        {
            // Skip if not English Kaikki entry
            if (string.IsNullOrWhiteSpace(rawDefinition) ||
                !KaikkiJsonHelper.IsEnglishEntry(rawDefinition))
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

            try
            {
                var etymology = KaikkiJsonHelper.ExtractEtymology(rawDefinition);

                if (!string.IsNullOrWhiteSpace(etymology))
                {
                    return new EtymologyExtractionResult
                    {
                        EtymologyText = CleanEtymologyText(etymology),
                        LanguageCode = DetectLanguageFromEtymology(etymology),
                        CleanedDefinition = definition, // Don't modify definition
                        DetectionMethod = "KaikkiStructuredEtymology",
                        SourceText = etymology
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract etymology for {Headword}", headword);
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

        private string? DetectLanguageFromEtymology(string etymology)
        {
            var languagePatterns = new Dictionary<string, string>
            {
                { @"\bLatin\b", "la" },
                { @"\bAncient Greek\b|\bGreek\b", "el" },
                { @"\bFrench\b", "fr" },
                { @"\bGerman(ic)?\b", "de" },
                { @"\bOld English\b", "ang" },
                { @"\bMiddle English\b", "enm" },
                { @"\bItalian\b", "it" },
                { @"\bSpanish\b", "es" },
                { @"\bDutch\b", "nl" },
                { @"\bProto-Indo-European\b", "ine-pro" },
                { @"\bOld Norse\b", "non" },
                { @"\bOld French\b", "fro" },
                { @"\bAnglo-Norman\b", "xno" }
            };

            foreach (var pattern in languagePatterns)
            {
                if (Regex.IsMatch(etymology, pattern.Key, RegexOptions.IgnoreCase))
                {
                    return pattern.Value;
                }
            }

            return null;
        }

        private string CleanEtymologyText(string etymology)
        {
            if (string.IsNullOrWhiteSpace(etymology))
                return string.Empty;

            // Clean up etymology text
            etymology = Regex.Replace(etymology, @"\s+", " ").Trim();

            // Remove template markers
            etymology = etymology
                .Replace("{{", "")
                .Replace("}}", "")
                .Replace("[[", "")
                .Replace("]]", "");

            // Remove HTML tags
            etymology = Regex.Replace(etymology, @"<[^>]+>", "");

            return etymology.Trim();
        }

        public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
        {
            // Not used for Kaikki - we need structured JSON
            return (null, null);
        }
    }
}