// WebsterEtymologyExtractor.cs
using DictionaryImporter.Core.Parsing;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Infrastructure.Parsing.EtymologyExtractor
{
    public sealed class WebsterEtymologyExtractor : IEtymologyExtractor
    {
        public string SourceCode => "GUT_WEBSTER";

        private readonly ILogger<WebsterEtymologyExtractor> _logger;

        // Pattern for "Etym: [text]"
        private static readonly Regex EtymRegex =
            new(@"Etym:\s*(?<etym>[^\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Pattern for "from Latin", "from Greek", etc.
        private static readonly Regex LanguageOriginRegex =
            new(@"(?:from|of|derived from|from the|from Old|from Middle)\s+(?<language>[A-Z][a-z]+(?: [A-Z][a-z]+)?)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Known language mappings
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

        public WebsterEtymologyExtractor(ILogger<WebsterEtymologyExtractor> logger)
        {
            _logger = logger;
        }

        public EtymologyExtractionResult Extract(
            string headword,
            string definition,
            string? rawDefinition = null)
        {
            if (string.IsNullOrWhiteSpace(definition))
            {
                return new EtymologyExtractionResult
                {
                    EtymologyText = null,
                    LanguageCode = null,
                    CleanedDefinition = definition,
                    DetectionMethod = "NoDefinition",
                    SourceText = string.Empty
                };
            }

            var cleanedHeadword = CleanHeadword(headword);
            var workingDefinition = definition;
            string? etymologyText = null;
            string? languageCode = null;
            string detectionMethod = "None";
            string sourceText = string.Empty;

            // First, try to extract using Etym: marker
            var etymMatch = EtymRegex.Match(workingDefinition);
            if (etymMatch.Success)
            {
                etymologyText = etymMatch.Groups["etym"].Value.Trim();
                sourceText = etymMatch.Value;

                // Remove the etymology marker from definition
                workingDefinition = workingDefinition
                    .Remove(etymMatch.Index, etymMatch.Length)
                    .Trim();

                detectionMethod = "EtymMarker";

                _logger.LogDebug(
                    "Etymology extracted via Etym: marker | Headword={Headword} | Etymology={Etymology}",
                    cleanedHeadword, etymologyText.Substring(0, Math.Min(50, etymologyText.Length)));
            }
            else
            {
                // Look for language origin patterns in definition
                var languageMatch = LanguageOriginRegex.Match(workingDefinition);
                if (languageMatch.Success)
                {
                    var languageText = languageMatch.Groups["language"].Value.Trim();
                    etymologyText = $"Derived from {languageText}";
                    sourceText = languageMatch.Value;
                    detectionMethod = "LanguageOrigin";

                    _logger.LogDebug(
                        "Language origin detected | Headword={Headword} | Language={Language}",
                        cleanedHeadword, languageText);
                }
            }

            // Extract language code from etymology text if found
            if (!string.IsNullOrWhiteSpace(etymologyText))
            {
                languageCode = ExtractLanguageCode(etymologyText);

                if (!string.IsNullOrWhiteSpace(languageCode))
                {
                    _logger.LogDebug(
                        "Language code extracted | Headword={Headword} | LanguageCode={LanguageCode}",
                        cleanedHeadword, languageCode);
                }
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

            // Try Etym: pattern first
            var etymMatch = EtymRegex.Match(text);
            if (etymMatch.Success)
            {
                etymology = etymMatch.Groups["etym"].Value.Trim();
            }
            else
            {
                // Try language origin pattern
                var languageMatch = LanguageOriginRegex.Match(text);
                if (languageMatch.Success)
                {
                    var languageText = languageMatch.Groups["language"].Value.Trim();
                    etymology = $"Derived from {languageText}";
                }
            }

            if (!string.IsNullOrWhiteSpace(etymology))
            {
                languageCode = ExtractLanguageCode(etymology);
            }

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

            // Check for known languages in the etymology text
            foreach (var mapping in LanguageMappings)
            {
                if (etymology.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.Value;
                }
            }

            // Check for language codes in parentheses
            var parenMatch = Regex.Match(etymology, @"\(([a-z]{2,3})\)");
            if (parenMatch.Success)
            {
                return parenMatch.Groups[1].Value.ToLowerInvariant();
            }

            // Check for abbreviations like "L." for Latin, "Gr." for Greek
            var abbrevMatch = Regex.Match(etymology, @"\b([A-Z]{1,3})\.\b");
            if (abbrevMatch.Success)
            {
                var abbrev = abbrevMatch.Groups[1].Value.ToLowerInvariant();
                return abbrev switch
                {
                    "l" or "lat" => "la",      // Latin
                    "gr" => "el",              // Greek
                    "fr" => "fr",              // French
                    "ger" => "de",             // German
                    "as" => "ang",             // Anglo-Saxon
                    "oe" => "ang",             // Old English
                    _ => null
                };
            }

            return null;
        }
    }
}