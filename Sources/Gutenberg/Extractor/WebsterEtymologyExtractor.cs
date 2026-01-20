namespace DictionaryImporter.Sources.Gutenberg.Extractor
{
    public sealed class WebsterEtymologyExtractor(ILogger<WebsterEtymologyExtractor> logger) : IEtymologyExtractor
    {
        private static readonly Regex EtymRegex =
            new(@"Etym:\s*(?<etym>[^\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LanguageOriginRegex =
            new(@"(?:from|of|derived from|from the|from Old|from Middle)\s+(?<language>[A-Z][a-z]+(?: [A-Z][a-z]+)?)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Dictionary<string, string> LanguageMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Latin", "la" },
            { "Greek", "el" },
            { "French", "fr" },
            { "German", "de" },
            { "Old English", "ang" },
            { "Middle English", "enm" },
            { "Anglo-Saxon", "ang" },
            { "AS", "ang" },
            { "Old French", "fro" },
            { "Italian", "it" },
            { "Spanish", "es" },
            { "Dutch", "nl" },
            { "Old Norse", "non" },
            { "Sanskrit", "sa" },
            { "Arabic", "ar" },
            { "Hebrew", "he" },
            { "Chinese", "zh" },
            { "Japanese", "ja" },
            { "Russian", "ru" }
        };

        public string SourceCode => "GUT_WEBSTER";

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

            var cleanedHeadword = CleanHeadword(headword);
            var workingDefinition = definition;
            string? etymologyText = null;
            string? languageCode = null;
            var detectionMethod = "None";
            var sourceText = string.Empty;

            var etymMatch = EtymRegex.Match(workingDefinition);
            if (etymMatch.Success)
            {
                etymologyText = etymMatch.Groups["etym"].Value.Trim();
                sourceText = etymMatch.Value;

                workingDefinition = workingDefinition
                    .Remove(etymMatch.Index, etymMatch.Length)
                    .Trim();

                detectionMethod = "EtymMarker";

                logger.LogDebug(
                    "Etymology extracted via Etym: marker | Headword={Headword} | Etymology={Etymology}",
                    cleanedHeadword, etymologyText.Substring(0, Math.Min(50, etymologyText.Length)));
            }
            else
            {
                var languageMatch = LanguageOriginRegex.Match(workingDefinition);
                if (languageMatch.Success)
                {
                    var languageText = languageMatch.Groups["language"].Value.Trim();
                    etymologyText = $"Derived from {languageText}";
                    sourceText = languageMatch.Value;
                    detectionMethod = "LanguageOrigin";

                    logger.LogDebug(
                        "Language origin detected | Headword={Headword} | Language={Language}",
                        cleanedHeadword, languageText);
                }
            }

            if (!string.IsNullOrWhiteSpace(etymologyText))
            {
                languageCode = ExtractLanguageCode(etymologyText);

                if (!string.IsNullOrWhiteSpace(languageCode))
                    logger.LogDebug(
                        "Language code extracted | Headword={Headword} | LanguageCode={LanguageCode}",
                        cleanedHeadword, languageCode);
            }

            return new EtymologyExtractionResult
            {
                EtymologyText = etymologyText,
                LanguageCode = languageCode,
                CleanedDefinition = workingDefinition,
                DetectionMethod = detectionMethod,
                SourceText = sourceText
            };
        }

        public (string? Etymology, string? LanguageCode) ExtractFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (null, null);

            string? etymology = null;
            string? languageCode = null;

            var etymMatch = EtymRegex.Match(text);
            if (etymMatch.Success)
            {
                etymology = etymMatch.Groups["etym"].Value.Trim();
            }
            else
            {
                var languageMatch = LanguageOriginRegex.Match(text);
                if (languageMatch.Success)
                {
                    var languageText = languageMatch.Groups["language"].Value.Trim();
                    etymology = $"Derived from {languageText}";
                }
            }

            if (!string.IsNullOrWhiteSpace(etymology)) languageCode = ExtractLanguageCode(etymology);

            return (etymology, languageCode);
        }

        private string CleanHeadword(string headword)
        {
            if (string.IsNullOrWhiteSpace(headword))
                return string.Empty;

            return headword
                .Replace("★", "")
                .Replace("☆", "")
                .Replace("●", "")
                .Replace("○", "")
                .Trim();
        }

        private string? ExtractLanguageCode(string etymology)
        {
            if (string.IsNullOrWhiteSpace(etymology))
                return null;

            foreach (var mapping in LanguageMappings)
                if (etymology.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                    return mapping.Value;

            var parenMatch = Regex.Match(etymology, @"\(([a-z]{2,3})\)");
            if (parenMatch.Success) return parenMatch.Groups[1].Value.ToLowerInvariant();

            var abbrevMatch = Regex.Match(etymology, @"\b([A-Z]{1,3})\.\b");
            if (abbrevMatch.Success)
            {
                var abbrev = abbrevMatch.Groups[1].Value.ToLowerInvariant();
                return abbrev switch
                {
                    "l" or "lat" => "la",
                    "gr" => "el",
                    "fr" => "fr",
                    "ger" => "de",
                    "as" => "ang",
                    "oe" => "ang",
                    _ => null
                };
            }

            return null;
        }
    }
}