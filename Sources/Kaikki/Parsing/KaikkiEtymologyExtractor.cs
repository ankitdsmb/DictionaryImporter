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
            try
            {
                // Try to extract from rawDefinition first (JSON)
                if (!string.IsNullOrWhiteSpace(rawDefinition) &&
                    rawDefinition.StartsWith("{") && rawDefinition.EndsWith("}"))
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawDefinition, options);

                    if (rawData != null && rawData.TryGetValue("etymology", out var etymElement))
                    {
                        var etymology = etymElement.GetString();
                        if (!string.IsNullOrWhiteSpace(etymology))
                        {
                            return new EtymologyExtractionResult
                            {
                                EtymologyText = CleanEtymologyText(etymology),
                                LanguageCode = DetectLanguageFromEtymology(etymology),
                                CleanedDefinition = definition,
                                DetectionMethod = "KaikkiJsonEtymology",
                                SourceText = etymology
                            };
                        }
                    }
                }

                // Fallback: extract from formatted definition
                if (!string.IsNullOrWhiteSpace(definition) && definition.Contains("【Etymology】"))
                {
                    var etymology = ExtractEtymologyFromDefinition(definition);
                    if (!string.IsNullOrWhiteSpace(etymology))
                    {
                        return new EtymologyExtractionResult
                        {
                            EtymologyText = etymology,
                            LanguageCode = DetectLanguageFromEtymology(etymology),
                            CleanedDefinition = RemoveEtymologyFromDefinition(definition),
                            DetectionMethod = "KaikkiFormattedEtymology",
                            SourceText = etymology
                        };
                    }
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
                DetectionMethod = "None",
                SourceText = string.Empty
            };
        }

        private string? ExtractEtymologyFromDefinition(string definition)
        {
            if (!definition.Contains("【Etymology】"))
                return null;

            var lines = definition.Split('\n');
            var inEtymologySection = false;
            var etymologyLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("【Etymology】"))
                {
                    inEtymologySection = true;
                    continue;
                }

                if (inEtymologySection)
                {
                    if (trimmedLine.StartsWith("【")) // New section started
                        break;

                    if (!trimmedLine.StartsWith("• "))
                    {
                        etymologyLines.Add(trimmedLine);
                    }
                }
            }

            return etymologyLines.Count > 0 ? string.Join(" ", etymologyLines) : null;
        }

        private string RemoveEtymologyFromDefinition(string definition)
        {
            if (!definition.Contains("【Etymology】"))
                return definition;

            var lines = definition.Split('\n');
            var result = new List<string>();
            var inEtymologySection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("【Etymology】"))
                {
                    inEtymologySection = true;
                    continue;
                }

                if (inEtymologySection)
                {
                    if (trimmedLine.StartsWith("【"))
                    {
                        inEtymologySection = false;
                        result.Add(trimmedLine);
                    }
                }
                else
                {
                    result.Add(trimmedLine);
                }
            }

            return string.Join("\n", result);
        }

        private string? DetectLanguageFromEtymology(string etymology)
        {
            var languagePatterns = new Dictionary<string, string>
            {
                { @"\bLatin\b", "la" },
                { @"\bAncient Greek\b|\bGreek\b", "el" },
                { @"\bFrench\b", "fr" },
                { @"\bGerman\b", "de" },
                { @"\bOld English\b", "ang" },
                { @"\bMiddle English\b", "enm" },
                { @"\bItalian\b", "it" },
                { @"\bSpanish\b", "es" },
                { @"\bDutch\b", "nl" },
                { @"\bProto-Indo-European\b", "ine-pro" }
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

            // Remove tree structure markers
            etymology = Regex.Replace(etymology, @"Proto-[^ ]+\s*\n?", " ");
            etymology = Regex.Replace(etymology, @"Etymology tree\s*\n?", " ");
            etymology = Regex.Replace(etymology, @"\bder\.\s*", " ");
            etymology = Regex.Replace(etymology, @"\blbor\.\s*", " ");

            return Regex.Replace(etymology, @"\s+", " ").Trim();
        }

        public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
        {
            var result = Extract("", text, text);
            return (result.EtymologyText, result.LanguageCode);
        }
    }
}