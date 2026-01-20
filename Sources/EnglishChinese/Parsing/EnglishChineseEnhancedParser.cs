using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.EnglishChinese.Parsing
{
    /// <summary>
    /// Enhanced parser for ENG_CHN format that extracts ALL dictionary fields
    /// </summary>
    public sealed class EnglishChineseEnhancedParser : ISourceDictionaryDefinitionParser
    {
        private readonly ILogger<EnglishChineseEnhancedParser> _logger;

        public EnglishChineseEnhancedParser(ILogger<EnglishChineseEnhancedParser> logger = null)
        {
            _logger = logger;
        }

        public string SourceCode => "ENG_CHN";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            if (entry == null)
            {
                yield return CreateFallbackParsedDefinition(entry);
                yield break;
            }

            // Use RawFragment if available
            var rawLine = !string.IsNullOrWhiteSpace(entry.RawFragment)
                ? entry.RawFragment
                : entry.Definition;

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                yield return CreateFallbackParsedDefinition(entry);
                yield break;
            }

            // Parse the complete entry
            var parsedEntry = ParseEngChnLine(rawLine, entry.Word);

            if (parsedEntry != null)
            {
                // Return main sense
                yield return CreateParsedDefinition(parsedEntry, entry.SenseNumber, rawLine);

                // Return additional senses if present
                if (parsedEntry.AdditionalSenses != null && parsedEntry.AdditionalSenses.Count > 0)
                {
                    var senseNumber = entry.SenseNumber + 1;
                    foreach (var additionalSense in parsedEntry.AdditionalSenses)
                    {
                        yield return CreateParsedDefinition(additionalSense, senseNumber++, rawLine);
                    }
                }
            }
            else
            {
                yield return CreateFallbackParsedDefinition(entry);
            }
        }

        private EngChnParsedEntry ParseEngChnLine(string rawLine, string headword)
        {
            var entry = new EngChnParsedEntry();

            // Split at ⬄ separator
            var parts = rawLine.Split('⬄', 2);
            if (parts.Length != 2)
            {
                // No separator, use entire line as definition
                entry.Definition = rawLine.Trim();
                return entry;
            }

            var leftPart = parts[0].Trim();
            var rightPart = parts[1].Trim();

            // Extract from left part (headword side)
            entry.Headword = leftPart;

            // Extract from right part (definition side)
            ParseDefinitionPart(rightPart, entry);

            return entry;
        }

        private void ParseDefinitionPart(string definitionPart, EngChnParsedEntry entry)
        {
            // Patterns in order of parsing priority
            var text = definitionPart;

            // 1. Extract syllabification (e.g., "18-wheel·er")
            var syllabificationMatch = Regex.Match(text, @"^([a-zA-Z·\-]+(?:\s+[a-zA-Z·\-]+)*?)(?=\s*\/)");
            if (syllabificationMatch.Success)
            {
                entry.Syllabification = syllabificationMatch.Groups[1].Value.Trim();
                text = text.Substring(syllabificationMatch.Length).TrimStart();
            }

            // 2. Extract IPA pronunciation (e.g., "/ˈeɪtiːnˌwhiːlə(r)/")
            var ipaMatch = Regex.Match(text, @"^\s*(/[^/]+/)\s*");
            if (ipaMatch.Success)
            {
                entry.IpaPronunciation = ipaMatch.Groups[1].Value.Trim();
                text = text.Substring(ipaMatch.Length).TrimStart();
            }

            // 3. Extract Part of Speech (e.g., "n.", "ad.", "a.", "abbr.")
            var posMatch = Regex.Match(text, @"^\s*(n\.|v\.|vt\.|vi\.|a\.|adj\.|ad\.|adv\.|prep\.|int\.|abbr\.|phr\.|pl\.|sing\.|comb\.form)\s*");
            if (posMatch.Success)
            {
                entry.PartOfSpeech = posMatch.Groups[1].Value.Trim();
                text = text.Substring(posMatch.Length).TrimStart();
            }

            // 4. Extract the main definition and all additional data
            ParseMainDefinitionAndMetadata(text, entry);
        }

        private void ParseMainDefinitionAndMetadata(string text, EngChnParsedEntry entry)
        {
            var remainingText = text;

            // Extract domain labels (〔医〕, 〔农〕, etc.)
            var domainMatches = Regex.Matches(remainingText, @"〔([^〕]+)〕");
            foreach (Match match in domainMatches)
            {
                entry.DomainLabels.Add(match.Groups[1].Value.Trim());
            }
            remainingText = Regex.Replace(remainingText, @"〔[^〕]+〕", "").Trim();

            // Extract register/region labels (〈口〉, 〈美〉, etc.)
            var registerMatches = Regex.Matches(remainingText, @"〈([^〉]+)〉");
            foreach (Match match in registerMatches)
            {
                entry.RegisterLabels.Add(match.Groups[1].Value.Trim());
            }
            remainingText = Regex.Replace(remainingText, @"〈[^〉]+〉", "").Trim();

            // Extract etymology (in square brackets)
            var etymologyMatch = Regex.Match(remainingText, @"\[\s*(?:<|字面意义：)([^\]]+)\]");
            if (etymologyMatch.Success)
            {
                entry.Etymology = etymologyMatch.Groups[1].Value.Trim();
                remainingText = remainingText.Replace(etymologyMatch.Value, "").Trim();
            }

            // Check for multiple senses (1., 2., etc.)
            var senseMatches = Regex.Matches(remainingText, @"(\d+)\.\s*([^0-9]+?)(?=(?:\d+\.|$))");

            if (senseMatches.Count > 0)
            {
                foreach (Match senseMatch in senseMatches)
                {
                    var senseNumber = senseMatch.Groups[1].Value;
                    var senseDefinition = senseMatch.Groups[2].Value.Trim();

                    if (senseNumber == "1")
                    {
                        // First sense is the main definition
                        entry.Definition = CleanDefinitionText(senseDefinition);
                    }
                    else
                    {
                        // Additional senses
                        var additionalSense = new EngChnParsedEntry
                        {
                            Headword = entry.Headword,
                            Syllabification = entry.Syllabification,
                            IpaPronunciation = entry.IpaPronunciation,
                            PartOfSpeech = entry.PartOfSpeech,
                            Definition = CleanDefinitionText(senseDefinition),
                            DomainLabels = new List<string>(entry.DomainLabels),
                            RegisterLabels = new List<string>(entry.RegisterLabels),
                            Etymology = entry.Etymology
                        };
                        entry.AdditionalSenses.Add(additionalSense);
                    }
                }
            }
            else
            {
                // Single sense
                entry.Definition = CleanDefinitionText(remainingText);
            }
        }

        private string CleanDefinitionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Remove trailing punctuation but preserve Chinese content
            var cleaned = text.Trim();

            // Clean up but preserve all meaningful characters
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        private ParsedDefinition CreateParsedDefinition(EngChnParsedEntry parsedEntry, int senseNumber, string rawFragment)
        {
            var parsed = new ParsedDefinition
            {
                MeaningTitle = parsedEntry.Headword ?? "unnamed sense",
                Definition = BuildFullDefinition(parsedEntry),
                RawFragment = rawFragment,
                SenseNumber = senseNumber,
                Domain = parsedEntry.DomainLabels.FirstOrDefault(),
                UsageLabel = BuildUsageLabel(parsedEntry),
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };

            // Add pronunciation to definition if present
            if (!string.IsNullOrWhiteSpace(parsedEntry.IpaPronunciation))
            {
                parsed.Definition = $"Pronunciation: {parsedEntry.IpaPronunciation}\n" + parsed.Definition;
            }

            return parsed;
        }

        private string BuildFullDefinition(EngChnParsedEntry entry)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(entry.Syllabification))
                parts.Add($"Syllabification: {entry.Syllabification}");

            if (!string.IsNullOrWhiteSpace(entry.PartOfSpeech))
                parts.Add($"({entry.PartOfSpeech})");

            parts.Add(entry.Definition ?? string.Empty);

            if (entry.DomainLabels.Count > 0)
                parts.Add($"【Domains】{string.Join(", ", entry.DomainLabels)}");

            if (entry.RegisterLabels.Count > 0)
                parts.Add($"【Registers】{string.Join(", ", entry.RegisterLabels)}");

            if (!string.IsNullOrWhiteSpace(entry.Etymology))
                parts.Add($"【Etymology】{entry.Etymology}");

            return string.Join(" ", parts).Trim();
        }

        private string BuildUsageLabel(EngChnParsedEntry entry)
        {
            var labels = new List<string>();

            if (!string.IsNullOrWhiteSpace(entry.PartOfSpeech))
                labels.Add(entry.PartOfSpeech);

            if (entry.RegisterLabels.Count > 0)
                labels.AddRange(entry.RegisterLabels);

            return labels.Count > 0 ? string.Join(", ", labels) : null;
        }

        private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry?.Word ?? "unnamed sense",
                Definition = entry?.Definition ?? string.Empty,
                RawFragment = entry?.RawFragment ?? entry?.Definition ?? string.Empty,
                SenseNumber = entry?.SenseNumber ?? 1,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

        // Internal model for parsed ENG_CHN entry
        private sealed class EngChnParsedEntry
        {
            public string Headword { get; set; }
            public string Syllabification { get; set; }
            public string IpaPronunciation { get; set; }
            public string PartOfSpeech { get; set; }
            public string Definition { get; set; }
            public List<string> DomainLabels { get; set; } = new List<string>();
            public List<string> RegisterLabels { get; set; } = new List<string>();
            public string Etymology { get; set; }
            public List<EngChnParsedEntry> AdditionalSenses { get; set; } = new List<EngChnParsedEntry>();
        }
    }
}