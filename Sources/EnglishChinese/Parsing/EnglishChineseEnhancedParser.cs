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
            _logger?.LogDebug(
                "EnglishChineseEnhancedParser.Parse called | Word={Word} | RawFragmentPreview={RawFragmentPreview}",
                entry?.Word ?? "null",
                entry?.RawFragment?.Substring(0, Math.Min(50, entry.RawFragment.Length)) ?? "null");

            if (entry == null)
            {
                _logger?.LogWarning("Parser received null entry");
                yield return CreateFallbackParsedDefinition(entry);
                yield break;
            }

            // Use RawFragment if available
            var rawLine = !string.IsNullOrWhiteSpace(entry.RawFragment)
                ? entry.RawFragment
                : entry.Definition;

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                _logger?.LogDebug("Empty raw line for entry: {Word}", entry.Word);
                yield return CreateFallbackParsedDefinition(entry);
                yield break;
            }

            // Parse the complete entry
            var parsedEntry = ParseEngChnLine(rawLine, entry.Word);

            if (parsedEntry != null)
            {
                _logger?.LogDebug(
                    "Successfully parsed entry | Word={Word} | Syllabification={Syllabification} | POS={POS} | DefLength={DefLength}",
                    entry.Word,
                    parsedEntry.Syllabification,
                    parsedEntry.PartOfSpeech,
                    parsedEntry.Definition?.Length ?? 0);

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
                _logger?.LogWarning("ParseEngChnLine returned null for: {Word}", entry.Word);
                yield return CreateFallbackParsedDefinition(entry);
            }
        }

        private EngChnParsedEntry ParseEngChnLine(string rawLine, string headword)
        {
            var entry = new EngChnParsedEntry();

            // ✅ FIX: Use the provided headword parameter, not extract from rawLine
            entry.Headword = headword ?? "unnamed sense";
            _logger?.LogDebug("ParseEngChnLine | Headword={Headword} | RawLine={RawLine}", entry.Headword, rawLine);

            // Split at ⬄ separator
            var parts = rawLine.Split('⬄', 2);
            if (parts.Length != 2)
            {
                // No separator, use entire line as definition
                entry.Definition = rawLine.Trim();
                _logger?.LogDebug("No ⬄ separator found, using entire line as definition");
                return entry;
            }

            // ✅ FIX: Only parse the definition part (right side)
            var rightPart = parts[1].Trim();
            _logger?.LogDebug("Found ⬄ separator | RightPart={RightPart}", rightPart);

            // Extract from right part (definition side)
            ParseDefinitionPart(rightPart, entry);

            return entry;
        }

        private void ParseDefinitionPart(string definitionPart, EngChnParsedEntry entry)
        {
            var text = definitionPart;
            _logger?.LogDebug("ParseDefinitionPart | Text={Text}", text);

            // ✅ FIX: Handle headwords with slashes like "24/7"
            // First try pattern for slash-containing headwords
            var slashHeadwordPattern = @"^([a-zA-Z0-9]+/[a-zA-Z0-9]+(?:\s+[a-zA-Z0-9]+/[a-zA-Z0-9]+)*?)\s+(/[^/]+/)";
            var slashHeadwordMatch = Regex.Match(text, slashHeadwordPattern);

            if (slashHeadwordMatch.Success)
            {
                // Case like "24/7 /pronunciation/"
                entry.Syllabification = slashHeadwordMatch.Groups[1].Value.Trim();
                entry.IpaPronunciation = slashHeadwordMatch.Groups[2].Value.Trim();
                text = text.Substring(slashHeadwordMatch.Length).TrimStart();

                _logger?.LogDebug("Found slash-headword syllabification: {Syllabification}", entry.Syllabification);
                _logger?.LogDebug("Found IPA for slash-headword: {IPA}", entry.IpaPronunciation);
            }
            else
            {
                // Original logic for normal cases without slash in headword
                var syllabificationMatch = Regex.Match(text, @"^([a-zA-Z0-9·\-]+(?:\s+[a-zA-Z0-9·\-]+)*?)(?=\s*\/[^/])");
                if (syllabificationMatch.Success)
                {
                    entry.Syllabification = syllabificationMatch.Groups[1].Value.Trim();
                    text = text.Substring(syllabificationMatch.Length).TrimStart();
                    _logger?.LogDebug("Found syllabification: {Syllabification}", entry.Syllabification);
                }

                var ipaMatch = Regex.Match(text, @"^\s*(/[^/]+/)\s*");
                if (ipaMatch.Success)
                {
                    entry.IpaPronunciation = ipaMatch.Groups[1].Value.Trim();
                    text = text.Substring(ipaMatch.Length).TrimStart();
                    _logger?.LogDebug("Found IPA: {IPA}", entry.IpaPronunciation);
                }
            }

            // Rest of the method remains the same...
            var posMatch = Regex.Match(text,
                @"^\s*(n\.|v\.|vt\.|vi\.|a\.|adj\.|ad\.|adv\.|prep\.|int\.|abbr\.|phr\.|pl\.|sing\.|comb\.form|conj\.|pron\.|det\.|interj\.|exclam\.|num\.|suffix|prefix)\s*",
                RegexOptions.IgnoreCase);

            if (posMatch.Success)
            {
                entry.PartOfSpeech = posMatch.Groups[1].Value.Trim();
                text = text.Substring(posMatch.Length).TrimStart();
                _logger?.LogDebug("Found POS: {POS}", entry.PartOfSpeech);
            }

            ParseMainDefinitionAndMetadata(text, entry);
        }

        private void ParseMainDefinitionAndMetadata(string text, EngChnParsedEntry entry)
        {
            var remainingText = text;
            _logger?.LogDebug("ParseMainDefinitionAndMetadata | Text={Text}", text);

            // Extract domain labels (〔医〕, 〔农〕, etc.)
            var domainMatches = Regex.Matches(remainingText, @"〔([^〕]+)〕");
            foreach (Match match in domainMatches)
            {
                entry.DomainLabels.Add(match.Groups[1].Value.Trim());
                _logger?.LogDebug("Found domain label: {Domain}", match.Groups[1].Value.Trim());
            }
            remainingText = Regex.Replace(remainingText, @"〔[^〕]+〕", "").Trim();

            // Extract register/region labels (〈口〉, 〈美〉, etc.)
            var registerMatches = Regex.Matches(remainingText, @"〈([^〉]+)〉");
            foreach (Match match in registerMatches)
            {
                entry.RegisterLabels.Add(match.Groups[1].Value.Trim());
                _logger?.LogDebug("Found register label: {Register}", match.Groups[1].Value.Trim());
            }
            remainingText = Regex.Replace(remainingText, @"〈[^〉]+〉", "").Trim();

            // Extract etymology (in square brackets)
            var etymologyMatch = Regex.Match(remainingText, @"\[\s*(?:<|字面意义：)([^\]]+)\]");
            if (etymologyMatch.Success)
            {
                entry.Etymology = etymologyMatch.Groups[1].Value.Trim();
                remainingText = remainingText.Replace(etymologyMatch.Value, "").Trim();
                _logger?.LogDebug("Found etymology: {Etymology}", entry.Etymology);
            }

            // Check for multiple senses (1., 2., etc.)
            var senseMatches = Regex.Matches(remainingText,
                @"(\d+)\.\s*(.+?)(?=(?:\d+\.|$))",
                RegexOptions.Singleline);

            if (senseMatches.Count > 0)
            {
                _logger?.LogDebug("Found {Count} numbered senses", senseMatches.Count);

                foreach (Match senseMatch in senseMatches)
                {
                    var senseNumber = senseMatch.Groups[1].Value;
                    var senseDefinition = senseMatch.Groups[2].Value.Trim();

                    _logger?.LogDebug("Sense {SenseNumber}: {SenseDefinitionPreview}",
                        senseNumber,
                        senseDefinition.Substring(0, Math.Min(50, senseDefinition.Length)));

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
                _logger?.LogDebug("Single sense definition: {DefinitionPreview}",
                    entry.Definition.Substring(0, Math.Min(50, entry.Definition.Length)));
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
                parsed.Definition = $"【Pronunciation】{parsedEntry.IpaPronunciation}\n" + parsed.Definition;
            }

            _logger?.LogDebug(
                "Created ParsedDefinition | Word={Word} | DefLength={Length} | HasChinese={HasChinese}",
                parsed.MeaningTitle,
                parsed.Definition.Length,
                ContainsChinese(parsed.Definition));

            return parsed;
        }

        private string BuildFullDefinition(EngChnParsedEntry entry)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(entry.Syllabification))
                parts.Add($"【Syllabification】{entry.Syllabification}");

            if (!string.IsNullOrWhiteSpace(entry.PartOfSpeech))
                parts.Add($"【POS】{entry.PartOfSpeech}");

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
            _logger?.LogWarning("Creating fallback ParsedDefinition for: {Word}", entry?.Word);

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

        private bool ContainsChinese(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.Any(c => c >= 0x4E00 && c <= 0x9FFF);
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