using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Common.Helper
{
    internal static class ParsingHelperCollins
    {
        #region Core Parsing Methods

        /// <summary>
        /// Parse a complete Collins entry into structured data
        /// </summary>
        public static CollinsParsedData ParseCollinsEntry(string definition)
        {
            var data = new CollinsParsedData();

            if (string.IsNullOrWhiteSpace(definition))
                return data;

            // Extract sense header with bilingual POS
            ParseSenseHeader(definition, data);

            // Extract main definition
            data.MainDefinition = ExtractMainDefinition(definition);

            // Extract all metadata sections
            data.DomainLabels = ExtractDomainLabels(definition);
            data.UsagePatterns = ExtractUsagePatterns(definition);
            data.Examples = ExtractExamples(definition);
            data.CrossReferences = ExtractCrossReferences(definition);
            data.PhrasalVerbInfo = ExtractPhrasalVerbInfo(definition);

            // Clean and normalize (English-only clean summary)
            data.CleanDefinition = CleanCollinsDefinition(definition, data);

            return data;
        }

        /// <summary>
        /// Parse the sense header (e.g., "1.N-VAR\t可变名词   definition...")
        /// </summary>
        private static void ParseSenseHeader(string definition, CollinsParsedData data)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return;

            // Pattern 1: Standard sense header with tab-separated POS
            // Example: "1.N-VAR\t可变名词   definition"
            var headerMatch = Regex.Match(definition,
                @"^(?<number>\d+)\.(?<pos>[A-Z\-]+)\t(?<pos_chinese>[^\t]+)\s+(?<definition>.+)",
                RegexOptions.Singleline);

            if (headerMatch.Success)
            {
                data.SenseNumber = int.TryParse(headerMatch.Groups["number"].Value, out var num) ? num : 1;
                data.PartOfSpeech = NormalizeCollinsPos(headerMatch.Groups["pos"].Value.Trim());
                data.ChinesePartOfSpeech = headerMatch.Groups["pos_chinese"].Value.Trim();
                data.RawDefinitionStart = headerMatch.Groups["definition"].Value.Trim();
                return;
            }

            // Pattern 2: Phrasal verb header
            // Example: "1.PHR-V\t短语动词   definition"
            var phrasalMatch = Regex.Match(definition,
                @"^(?<number>\d+)\.(?<pos>PHR-[A-Z]+)\t(?<pos_chinese>[^\t]+)\s+(?<definition>.+)",
                RegexOptions.Singleline);

            if (phrasalMatch.Success)
            {
                data.SenseNumber = int.TryParse(phrasalMatch.Groups["number"].Value, out var num) ? num : 1;
                data.PartOfSpeech = "phrasal_verb";
                data.ChinesePartOfSpeech = phrasalMatch.Groups["pos_chinese"].Value.Trim();
                data.RawDefinitionStart = phrasalMatch.Groups["definition"].Value.Trim();
                data.IsPhrasalVerb = true;
                return;
            }

            // Pattern 3: Simple numbered line (fallback)
            var simpleMatch = Regex.Match(definition,
                @"^(?<number>\d+)\.\s+(?<definition>.+)",
                RegexOptions.Singleline);

            if (simpleMatch.Success)
            {
                data.SenseNumber = int.TryParse(simpleMatch.Groups["number"].Value, out var num) ? num : 1;
                data.PartOfSpeech = "unk";
                data.RawDefinitionStart = simpleMatch.Groups["definition"].Value.Trim();
            }
        }

        /// <summary>
        /// Extract the main English definition text.
        /// </summary>
        public static string ExtractMainDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var lines = definition.Split('\n');
            CollinsSenseRaw? firstSense = null;

            // 1) Find first sense header line using extractor (full line, not trimmed fragments)
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (TryParseSenseHeader(trimmed, out var sense) && sense != null)
                {
                    firstSense = sense;
                    break;
                }
            }

            if (firstSense == null)
                return string.Empty;

            // 2) Clean only the sense header definition (do not remove too aggressively)
            var cleaned = CleanDefinitionText(firstSense.Definition);

            // 3) Ensure sentence structure is stable
            cleaned = EnsureSentenceStructure(cleaned);

            return cleaned;
        }

        /// <summary>
        /// Extract domain/register labels (语域标签)
        /// </summary>
        public static IReadOnlyList<string> ExtractDomainLabels(string definition)
        {
            var labels = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return labels;

            var labelMatches = Regex.Matches(definition,
                @"【语域标签】：\s*(?<labels>[^【\n]+)");

            foreach (Match match in labelMatches)
            {
                var labelText = match.Groups["labels"].Value.Trim();
                var splitLabels = Regex.Split(labelText, @"[\s,，]+");

                foreach (var label in splitLabels)
                {
                    var cleanLabel = label.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanLabel))
                        labels.Add(cleanLabel);
                }
            }

            // Add some common inferred domain markers
            if (definition.Contains("INFORMAL") && !labels.Any(l => l.Contains("INFORMAL", StringComparison.OrdinalIgnoreCase)))
                labels.Add("INFORMAL");

            if (definition.Contains("FORMAL") && !labels.Any(l => l.Contains("FORMAL", StringComparison.OrdinalIgnoreCase)))
                labels.Add("FORMAL");

            if (definition.Contains("主美") && !labels.Any(l => l.Equals("US", StringComparison.OrdinalIgnoreCase)))
                labels.Add("US");

            if (definition.Contains("主英") && !labels.Any(l => l.Equals("UK", StringComparison.OrdinalIgnoreCase)))
                labels.Add("UK");

            return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Extract usage patterns (搭配模式)
        /// </summary>
        public static IReadOnlyList<string> ExtractUsagePatterns(string definition)
        {
            var patterns = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return patterns;

            var patternMatches = Regex.Matches(definition,
                @"【搭配模式】：\s*(?<pattern>[^【\n]+)");

            foreach (Match match in patternMatches)
            {
                var pattern = match.Groups["pattern"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(pattern))
                    patterns.Add(pattern);
            }

            return patterns;
        }

        /// <summary>
        /// Extract examples (starting with "..." or indented English)
        /// </summary>
        public static IReadOnlyList<string> ExtractExamples(string definition)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return examples;

            var lines = definition.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // Primary Collins example marker
                if (TryParseExample(trimmed, out var ex))
                {
                    var cleaned = CleanExampleText(ex);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                        examples.Add(cleaned);
                }
            }

            return examples.Distinct().ToList();
        }

        /// <summary>
        /// Extract cross-references (→see: word)
        /// </summary>
        public static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
        {
            var crossRefs = new List<CrossReference>();

            if (string.IsNullOrWhiteSpace(definition))
                return crossRefs;

            var seeMatches = Regex.Matches(definition,
                @"→see:\s*(?<word>\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b)",
                RegexOptions.IgnoreCase);

            foreach (Match match in seeMatches)
            {
                var targetWord = match.Groups["word"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(targetWord))
                {
                    crossRefs.Add(new CrossReference
                    {
                        TargetWord = targetWord,
                        ReferenceType = "See"
                    });
                }
            }

            if (definition.Contains("See also:", StringComparison.OrdinalIgnoreCase))
            {
                var seeAlsoMatch = Regex.Match(definition,
                    @"See also:\s*(?<words>[^.]+)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);

                if (seeAlsoMatch.Success)
                {
                    var wordsText = seeAlsoMatch.Groups["words"].Value.Trim();
                    var words = wordsText.Split(',', ';', '、');

                    foreach (var word in words)
                    {
                        var cleanWord = word.Trim();
                        if (!string.IsNullOrWhiteSpace(cleanWord) && cleanWord.Length > 1)
                        {
                            crossRefs.Add(new CrossReference
                            {
                                TargetWord = cleanWord,
                                ReferenceType = "SeeAlso"
                            });
                        }
                    }
                }
            }

            return crossRefs;
        }

        /// <summary>
        /// Extract phrasal verb information
        /// </summary>
        public static PhrasalVerbInfo ExtractPhrasalVerbInfo(string definition)
        {
            var info = new PhrasalVerbInfo();

            if (string.IsNullOrWhiteSpace(definition))
                return info;

            // NEW: Collins data includes PHR-V, PHR V, and PHRASAL VERB
            if (!(definition.Contains("PHR-V", StringComparison.OrdinalIgnoreCase) ||
                  definition.Contains("PHR V", StringComparison.OrdinalIgnoreCase) ||
                  definition.Contains("PHRASAL VERB", StringComparison.OrdinalIgnoreCase) ||
                  definition.Contains("短语动词", StringComparison.OrdinalIgnoreCase)))
            {
                return info;
            }

            info.IsPhrasalVerb = true;

            var particleMatch = Regex.Match(definition,
                @"\b(PHR-V|PHR\s+V|PHRASAL VERB)\s+(?<verb>\w+)\s+(?<particle>\w+)\b",
                RegexOptions.IgnoreCase);

            if (particleMatch.Success)
            {
                info.Verb = particleMatch.Groups["verb"].Value;
                info.Particle = particleMatch.Groups["particle"].Value;
            }

            info.Patterns = ExtractUsagePatterns(definition);
            return info;
        }

        /// <summary>
        /// Extract synonyms from definition and examples
        /// </summary>
        public static IReadOnlyList<string> ExtractSynonyms(string definition, IReadOnlyList<string> examples)
        {
            var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(definition))
                return synonyms.ToList();

            var alsoCalledMatches = Regex.Matches(definition,
                @"(?:also called|also known as|synonymous with|same as)\s+(?<word>\b[A-Z][a-z]+\b)",
                RegexOptions.IgnoreCase);

            foreach (Match match in alsoCalledMatches)
            {
                var synonym = match.Groups["word"].Value.ToLowerInvariant();
                if (synonym.Length > 2)
                    synonyms.Add(synonym);
            }

            var parentheticalMatches = Regex.Matches(definition,
                @"\(also\s+(?<word>\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\b",
                RegexOptions.IgnoreCase);

            foreach (Match match in parentheticalMatches)
            {
                var words = match.Groups["word"].Value.Split(' ', ';', ',');
                foreach (var word in words)
                {
                    var synonym = word.Trim().ToLowerInvariant();
                    if (synonym.Length > 2)
                        synonyms.Add(synonym);
                }
            }

            foreach (var example in examples ?? new List<string>())
            {
                var exampleSynonyms = ExtractSynonymsFromExample(example);
                foreach (var synonym in exampleSynonyms)
                {
                    synonyms.Add(synonym);
                }
            }

            return synonyms.ToList();
        }

        private static IReadOnlyList<string> ExtractSynonymsFromExample(string example)
        {
            var synonyms = new List<string>();

            if (string.IsNullOrWhiteSpace(example))
                return synonyms;

            var synonymPatterns = new[]
            {
                @"\b(?<word1>\w+)\s+means\s+(?<word2>\w+)\b",
                @"\b(?<word1>\w+)\s+is\s+(?<word2>\w+)\b",
                @"\b(?<word1>\w+)\s+and\s+(?<word2>\w+)\s+are\s+synonyms\b",
                @"\b(?<word1>\w+)\s*,\s*or\s+(?<word2>\w+)\b"
            };

            foreach (var pattern in synonymPatterns)
            {
                var match = Regex.Match(example, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    if (match.Groups["word1"].Success)
                        synonyms.Add(match.Groups["word1"].Value.ToLowerInvariant());
                    if (match.Groups["word2"].Success)
                        synonyms.Add(match.Groups["word2"].Value.ToLowerInvariant());
                }
            }

            return synonyms;
        }

        #endregion

        #region Cleaning and Normalization Methods

        /// <summary>
        /// Clean Collins definition text (English-only output)
        /// </summary>
        public static string CleanCollinsDefinition(string definition, CollinsParsedData data = null)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var cleaned = definition;

            // Remove Collins star markers
            cleaned = Regex.Replace(cleaned, @"^★+☆+\s*", "");

            // Remove sense number and POS header
            // OLD: cleaned = Regex.Replace(cleaned, @"^\d+\.[A-Z\-]+\t[^\t]+\s+", "");
            // FIX: POS header can contain spaces and semicolons (keep flexible)
            cleaned = Regex.Replace(cleaned, @"^\d+\.[A-Z][A-Z\-\s;]+\t[^\t]+\s+", "", RegexOptions.Singleline);

            // Remove 【Sections】
            var sectionsToRemove = new[]
            {
                "【语域标签】：", "【搭配模式】：", "【Examples】", "【SeeAlso】",
                "【Usage】", "【Grammar】", "【Pronunciation】", "【Etymology】"
            };

            foreach (var section in sectionsToRemove)
            {
                cleaned = RemoveCollinsSection(cleaned, section);
            }

            // Remove cross-reference markers
            cleaned = Regex.Replace(cleaned, @"→see:\s*\w+", "");

            // Clean up example markers
            cleaned = Regex.Replace(cleaned, @"^\.\.\.\s*", "", RegexOptions.Multiline);

            // Remove Chinese translations and keep English part only
            var lines = cleaned.Split('\n');
            var englishLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // If line has Chinese, attempt to keep best English prefix portion
                if (ContainsChineseCharacters(trimmed))
                {
                    var englishPart = ExtractLeadingEnglishSegment(trimmed);
                    if (!string.IsNullOrWhiteSpace(englishPart))
                        englishLines.Add(englishPart);
                }
                else
                {
                    englishLines.Add(trimmed);
                }
            }

            cleaned = string.Join(" ", englishLines);

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            cleaned = cleaned.TrimEnd('.', ',', ';', ':');

            if (!string.IsNullOrEmpty(cleaned) && !cleaned.EndsWith(".", StringComparison.Ordinal))
                cleaned += ".";

            return cleaned;
        }

        private static string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return example;

            var cleaned = example.Trim();

            if (cleaned.StartsWith("..."))
                cleaned = cleaned.Substring(3).Trim();

            if (cleaned.Contains("。"))
                cleaned = cleaned.Split('。')[0].Trim();

            cleaned = cleaned.Trim();

            if (!string.IsNullOrEmpty(cleaned))
            {
                if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
                    cleaned += ".";

                if (cleaned.Length > 0 && char.IsLower(cleaned[0]))
                    cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
            }

            return cleaned;
        }

        private static string CleanDefinitionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var cleaned = text;

            cleaned = Regex.Replace(cleaned, @"→see:\s*\w+", "");
            cleaned = Regex.Replace(cleaned, @"^\.\.\.\s*", "", RegexOptions.Multiline);

            cleaned = RemoveChineseText(cleaned);

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            // Final safety: remove trailing weird punctuation fragments
            cleaned = cleaned.Trim(' ', '.', ',', ';', ':');

            return cleaned;
        }

        private static string NormalizeCollinsPos(string posCode)
        {
            return posCode.ToUpperInvariant() switch
            {
                "N-VAR" or "N-COUNT" or "N-UNCOUNT" or "N-MASS" or "N" => "noun",
                "V" or "V-T" or "V-I" or "V-ERG" => "verb",
                "ADJ" or "ADJ-GRADED" => "adjective",
                "ADV" => "adverb",
                "PHR-V" or "PHRASAL VERB" => "phrasal_verb",
                "PHR" or "PHRASE" => "phrase",
                "PREP" => "preposition",
                "CONJ" => "conjunction",
                "PRON" => "pronoun",
                "DET" => "determiner",
                "INT" or "INTERJ" => "interjection",
                "NUM" => "numeral",
                "ABBR" => "abbreviation",
                "PREFIX" => "prefix",
                "SUFFIX" => "suffix",
                "COMB" or "COMBINING FORM" => "combining_form",
                _ => "unk"
            };
        }

        #endregion

        #region Helper Methods

        private static string RemoveCollinsSection(string text, string sectionMarker)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(sectionMarker))
                return text;

            var index = text.IndexOf(sectionMarker, StringComparison.Ordinal);
            if (index < 0)
                return text;

            var afterMarker = text.Substring(index + sectionMarker.Length);
            var endIndex = afterMarker.IndexOf("【", StringComparison.Ordinal);

            if (endIndex >= 0)
                return text.Substring(0, index) + afterMarker.Substring(endIndex);

            return text.Substring(0, index).Trim();
        }

        private static bool ContainsChineseCharacters(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.Any(c => c >= 0x4E00 && c <= 0x9FFF);
        }

        private static string RemoveChineseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Remove Chinese characters and punctuation
            var chineseRegex = new Regex(@"[\u4E00-\u9FFF\u3000-\u303F\uFF00-\uFFEF]+");
            var cleaned = chineseRegex.Replace(text, " ");

            cleaned = cleaned.Replace("【", "").Replace("】", "")
                .Replace("〈", "").Replace("〉", "");

            return Regex.Replace(cleaned, @"\s+", " ").Trim();
        }

        #endregion

        #region Compiled Regex Patterns (Optimized for Performance)

        public static readonly Regex CfRegex = new(@"\bCf\.?\s+(?<target>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex SeeAlsoRegex = new(@"\bSee also:\s*(?<targets>[^.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex SeeRegex = new(@"\bSee:\s*(?<target>[^.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex CrossReferenceRegex = new(@"^(?<word>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)→see:\s*(?<target>[a-z]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex SynonymPatternRegex = new(
            @"\b(?:synonymous|synonym|same as|equivalent to|also called)\s+(?:[\w\s]*?\s)?(?<word>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex ParentheticalSynonymRegex = new(@"\b(?<word>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\s*\((?:also|syn|syn\.|synonym)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex OrSynonymRegex = new(@"\b(?<word1>[A-Z][a-z]+)\s+or\s+(?<word2>[A-Z][a-z]+)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex WordBoundaryRegex = new(@"\b[A-Z][a-z]+\b",
            RegexOptions.Compiled);

        public static readonly Regex EnglishTextRegex = new(@"^[A-Za-z0-9\-\s]+",
            RegexOptions.Compiled);

        public static readonly Regex EnglishSentenceRegex = new(@"[A-Z][^\.!?]*[\.!?]",
            RegexOptions.Compiled);

        private static readonly Regex ChineseCharRegex = new(@"[\u4E00-\u9FFF\u3040-\u309F\u30A0-\u30FF]",
            RegexOptions.Compiled);

        private const string ExampleSeparator = "【Examples】";
        private const string NoteSeparator = "【Note】";
        private const string DomainSeparator = "【Domain】";
        private const string GrammarSeparator = "【Grammar】";

        public static readonly Regex ExampleBulletRegex = new(@"^•\s*(.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public static readonly Regex DomainLabelRegex = new(@"【([^】]+)】：\s*(.+)",
            RegexOptions.Compiled);

        public static readonly Regex LabelRegex = new(@"【([^】]+)】[:：]?\s*(.+)",
            RegexOptions.Compiled);

        public static readonly Regex HeadwordRegex = new(
            @"^★+☆+\s+([A-Za-z][A-Za-z\-\s]+?)\s+●+○+",
            RegexOptions.Compiled);

        public static readonly Regex SenseHeaderEnhancedRegex = new(
            @"^(?:(?<number>\d+)\.\s*)?(?<pos>[A-Z][A-Z\-\s;]+)(?:[/\\]\s*[A-Z][A-Z\-\s;]*)?\t[^\x00-\x7F]*\s*(?<definition>.+)",
            RegexOptions.Compiled);

        public static readonly Regex SenseNumberOnlyRegex = new(@"^(\d+)\.\s*(.+)",
            RegexOptions.Compiled);

        public static readonly Regex GrammarCodeWithSlashRegex = new(@"^([A-Z][A-Z\-\s;]+)[\t\s]*[/\\]\s*(.+)",
            RegexOptions.Compiled);

        public static readonly Regex ExampleRegex = new(@"^(?:\.{2,}|…)\s*(?<example>[A-Z].*?[.!?])(?:\s*[^\x00-\x7F]*)?$",
            RegexOptions.Compiled);

        public static readonly Regex SimpleExampleRegex = new(@"^[A-Z][^.!?]*[.!?](?:\s*[^\x00-\x7F]*)?$",
            RegexOptions.Compiled);

        public static readonly Regex EllipsisOrDotsRegex = new(@"^(\.{2,}|…)",
            RegexOptions.Compiled);

        public static readonly Regex PhrasalVerbRegex = new(@"^(?:PHRASAL\s+VERB|PHR\s+V)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // NOTE: This regex only captures definition, so caller must not assume it captures POS.
        public static readonly Regex PhrasalPatternRegex = new(@"^(?:PHR(?:-[A-Z]+)+|PHRASAL VERB)\s+(?<definition>[A-Z].+)",
            RegexOptions.Compiled);

        public static readonly Regex NumberedSectionRegex = new(@"^\d+\.\s+[A-Z][A-Z\s]+USES",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex GrammarCodeRegex = new(
            @"\b(?:V-ERG|N-COUNT|N-UNCOUNT|ADJ-GRADED|PHR-CONJ-SUBORD|PHR-V|PHR-ADV|PHR-PREP|V-TRANS|V-INTRANS|V-REFL|V-PASS)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LeadingJunkRegex = new(@"^[\s、。，；,\.\(\)（）:：…\/\\;]+",
            RegexOptions.Compiled);

        private static readonly Regex ExtraSpacesRegex = new(@"\s{2,}",
            RegexOptions.Compiled);

        private static readonly char[] TrimChars = ['.', '。', ' ', '…', '"', '\''];
        private static readonly char[] SeparatorChars = [',', ';', '，', '；'];
        private static readonly string[] SectionMarkers = ["【Examples】", "【Note】", "【Domain】", "【Grammar】"];

        #endregion

        #region Constants and Lookup Tables

        private static readonly Dictionary<string, string> DomainCodeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AM"] = "US",
            ["US"] = "US",
            ["BRIT"] = "UK",
            ["UK"] = "UK",
            ["主美"] = "US",
            ["美式"] = "US",
            ["主英"] = "UK",
            ["英式"] = "UK",
            ["FORMAL"] = "FORMAL",
            ["正式"] = "FORMAL",
            ["INFORMAL"] = "INFORMAL",
            ["非正式"] = "INFORMAL",
            ["LITERARY"] = "LITERARY",
            ["文学"] = "LITERARY",
            ["OLD-FASHIONED"] = "OLD-FASHIONED",
            ["过时"] = "OLD-FASHIONED",
            ["TECHNICAL"] = "TECHNICAL",
            ["技术"] = "TECHNICAL",
            ["术语"] = "TECHNICAL",
            ["RARE"] = "RARE",
            ["罕见"] = "RARE",
            ["OBSOLETE"] = "OBSOLETE",
            ["废弃"] = "OBSOLETE",
            ["ARCHAIC"] = "ARCHAIC",
            ["古语"] = "ARCHAIC",
            ["COLLOQUIAL"] = "COLLOQUIAL",
            ["口语"] = "COLLOQUIAL",
            ["SLANG"] = "SLANG",
            ["俚语"] = "SLANG",
            ["VULGAR"] = "VULGAR",
            ["OFFENSIVE"] = "OFFENSIVE",
            ["HUMOROUS"] = "HUMOROUS"
        };

        private static readonly Dictionary<string, string> ChineseDomainMap = new()
        {
            ["主美"] = "US",
            ["美式"] = "US",
            ["主英"] = "UK",
            ["英式"] = "UK",
            ["正式"] = "FORMAL",
            ["非正式"] = "INFORMAL",
            ["文学"] = "LITERARY",
            ["过时"] = "OLD-FASHIONED",
            ["技术"] = "TECHNICAL",
            ["罕见"] = "RARE",
            ["废弃"] = "OBSOLETE",
            ["古语"] = "ARCHAIC",
            ["口语"] = "COLLOQUIAL",
            ["俚语"] = "SLANG"
        };

        private static readonly Dictionary<string, string> PosNormalizationMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["N"] = "noun",
            ["N-COUNT"] = "noun",
            ["N-UNCOUNT"] = "noun",
            ["N-MASS"] = "noun",
            ["N-VAR"] = "noun",
            ["N-PLURAL"] = "noun",
            ["N-SING"] = "noun",
            ["N-PL"] = "noun",
            ["N-PROPER"] = "noun",
            ["N-TITLE"] = "noun",
            ["N-VOC"] = "noun",
            ["N-COUNT; N-IN-NAMES"] = "noun",
            ["N-COUNT & N-UNCOUNT"] = "noun",
            ["N-COUNT OR N-UNCOUNT"] = "noun",
            ["VERB"] = "verb",
            ["V"] = "verb",
            ["V-LINK"] = "verb",
            ["V-ERG"] = "verb",
            ["V-RECIP"] = "verb",
            ["V-T"] = "verb",
            ["V-I"] = "verb",
            ["V-PASS"] = "verb",
            ["V-ERG / V-RECIP"] = "verb",
            ["V-T / V-I"] = "verb",
            ["V-RECIP-ERG"] = "verb",
            ["V-ERG-RECIP"] = "verb",
            ["ADJ"] = "adj",
            ["ADJ-GRADED"] = "adj",
            ["ADJ-COMPAR"] = "adj",
            ["ADJ-SUPERL"] = "adj",
            ["ADJ CLASSIF"] = "adj",
            ["ADV"] = "adv",
            ["PHR-ADV"] = "adv",
            ["PHRASAL VERB"] = "phrasal_verb",
            ["PHRASAL VB"] = "phrasal_verb",
            ["PHR V"] = "phrasal_verb",
            ["PHR"] = "phrase",
            ["PHRASE"] = "phrase",
            ["PREP"] = "preposition",
            ["PHR-PREP"] = "preposition",
            ["CONJ"] = "conjunction",
            ["PHR-CONJ-SUBORD"] = "conjunction",
            ["PRON"] = "pronoun",
            ["DET"] = "determiner",
            ["DETERMINER"] = "determiner",
            ["ARTICLE"] = "determiner",
            ["EXCLAM"] = "exclamation",
            ["EXCL"] = "exclamation",
            ["INTERJ"] = "exclamation",
            ["INTERJECTION"] = "exclamation",
            ["SUFFIX"] = "suffix",
            ["PREFIX"] = "prefix",
            ["COMB"] = "combining_form",
            ["COMBINING FORM"] = "combining_form",
            ["NUM"] = "numeral",
            ["NUMERAL"] = "numeral",
            ["AUX"] = "auxiliary",
            ["AUXILIARY"] = "auxiliary",
            ["MODAL"] = "modal",
            ["ADJ COLOR"] = "adj",
            ["ADJ COLOR PRED"] = "adj",
            ["ADJ PRED"] = "adj",
            ["ADV-GRADED"] = "adv",
            ["CONJ COORD"] = "conjunction",
            ["CONJ SUBORD"] = "conjunction",
            ["N-COUNT-COLL"] = "noun",
            ["N-FAMILY"] = "noun",
            ["N-IN-NAMES"] = "noun",
            ["N-PART"] = "noun",
            ["N-PROPER-PL"] = "noun",
            ["N-SING-COLL"] = "noun",
            ["PHR-CONJ"] = "conjunction",
            ["PHR-PART"] = "particle",
            ["PHR-PRON"] = "pronoun",
            ["PHR-QUANT"] = "quantifier",
            ["QUANT"] = "quantifier",
            ["QUANTIFIER"] = "quantifier",
            ["V-ERG-ADJ"] = "verb",
            ["V-ERG-N"] = "verb",
            ["V-LINK-ADJ"] = "verb",
            ["V-LINK-N"] = "verb",
            ["V-LINK-PHR"] = "verb",
            ["V-PASS-ADJ"] = "verb",
            ["V-RECIP-ADJ"] = "verb"
        };

        private static readonly HashSet<string> PosPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "N-", "V-", "ADJ-", "ADV-", "PHR-", "PREP", "CONJ", "PRON",
            "DET", "EXCLAM", "EXCL", "INTERJ", "SUFFIX", "PREFIX",
            "COMB", "NUM", "AUX", "MODAL", "QUANT"
        };

        #endregion

        #region CollinsExtractor Helper Methods

        public static bool IsEntrySeparator(string line)
        {
            return !string.IsNullOrEmpty(line) &&
                   (line.StartsWith("——————————————", StringComparison.Ordinal) ||
                    line.StartsWith("---------------", StringComparison.Ordinal));
        }

        public static bool TryParseHeadword(string line, out string headword)
        {
            headword = string.Empty;
            if (string.IsNullOrEmpty(line)) return false;

            if (line.StartsWith("★"))
            {
                headword = Regex.Replace(line, @"^[★☆●○▶\.\s]+", "").Trim();
                return !string.IsNullOrEmpty(headword);
            }

            return false;
        }

        public static bool TryParseSenseHeader(string line, out CollinsSenseRaw sense)
        {
            sense = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (line.StartsWith("or ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("(")) return false;

            var parsingStrategies = new List<Func<string, CollinsSenseRaw?>>
            {
                TryParseEnhancedSenseHeader,
                TryParseGrammarCodeWithSlash,
                TryParsePhrasalPattern,
                TryParseDoubleSemicolonFormat,
                TryParseSimpleNumberedLine
            };

            foreach (var result in parsingStrategies.Select(strategy => strategy(line)).OfType<CollinsSenseRaw>())
            {
                sense = result;
                return true;
            }

            return false;
        }

        public static bool TryParseExample(string line, out string example)
        {
            example = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;

            if (line.StartsWith("..."))
            {
                example = line.Substring(3).Trim();
                return !string.IsNullOrWhiteSpace(example);
            }

            if (EllipsisOrDotsRegex.IsMatch(line))
            {
                example = line.TrimStart('.', '…', ' ').Trim();
                return !string.IsNullOrWhiteSpace(example);
            }

            return false;
        }

        public static bool TryParseUsageNote(string line, out string usageNote)
        {
            if (line != null && (line.StartsWith("Note that", StringComparison.OrdinalIgnoreCase) ||
                                 line.StartsWith("Usage Note", StringComparison.OrdinalIgnoreCase)))
            {
                usageNote = line;
                return true;
            }

            usageNote = string.Empty;
            return false;
        }

        public static bool TryParseDomainLabel(string line, out (string LabelType, string Value) labelInfo)
        {
            if (!string.IsNullOrEmpty(line) && line.StartsWith('【'))
            {
                var match = DomainLabelRegex.Match(line);
                if (match.Success)
                {
                    labelInfo = (match.Groups[1].Value, match.Groups[2].Value);
                    return true;
                }

                match = Regex.Match(line, @"【([^】]+)】\s*(.+)");
                if (match.Success)
                {
                    labelInfo = (match.Groups[1].Value, match.Groups[2].Value);
                    return true;
                }
            }

            labelInfo = (string.Empty, string.Empty);
            return false;
        }

        public static bool IsDefinitionContinuation(string line, string currentDefinition)
        {
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(currentDefinition))
                return false;

            var trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            if (Regex.IsMatch(trimmed, @"^\d+\.\s+[A-Z]"))
                return false;

            if (trimmed.Contains("→see:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("See:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("See also:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (Regex.IsMatch(trimmed, @"^【[^】]+】"))
                return false;

            if (trimmed.Length > 0 && char.IsLower(trimmed[0]))
                return true;

            if (trimmed.StartsWith("or ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("but ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("especially ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("particularly ", StringComparison.OrdinalIgnoreCase))
                return true;

            if (trimmed.StartsWith("(") || trimmed.StartsWith("-") || trimmed.StartsWith("—"))
                return true;

            if (currentDefinition.EndsWith(",") ||
                currentDefinition.EndsWith(";") ||
                currentDefinition.EndsWith(":"))
            {
                if (trimmed.Length > 0 && char.IsLetter(trimmed[0]))
                    return true;
            }

            return false;
        }

        #endregion

        #region Text Processing and Cleaning

        public static string RemoveChineseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return ChineseCharRegex.Replace(text, "");
        }

        public static string CleanText(string text, bool removeChinese = false, bool normalizePunctuation = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var result = text;

            if (removeChinese)
                result = ChineseCharRegex.Replace(result, "");

            if (normalizePunctuation)
            {
                result = LeadingJunkRegex.Replace(result, "");

                if (result.Contains("、")) result = result.Replace("、", ", ");
                if (result.Contains("。")) result = result.Replace("。", ". ");
                if (result.Contains("；")) result = result.Replace("；", "; ");
                if (result.Contains("：")) result = result.Replace("：", ": ");
                if (result.Contains("…")) result = result.Replace("…", "...");
                if (result.Contains("．．．")) result = result.Replace("．．．", "...");

                result = result.Replace(" ,", ",").Replace(" .", ".")
                    .Replace(" ;", ";").Replace(" :", ":")
                    .Replace("( ", "(").Replace(" )", ")")
                    .Replace("?.", "?").Replace("!.", "!")
                    .Replace("..", ".");
            }

            if (result.Contains(" "))
                result = ExtraSpacesRegex.Replace(result, " ");

            return result.Trim();
        }

        public static string? ExtractCleanDomain(string? domainText)
        {
            if (string.IsNullOrWhiteSpace(domainText)) return null;

            var text = domainText.Trim();

            foreach (var kvp in ChineseDomainMap)
                if (text.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase)) return kvp.Value;

            foreach (var kvp in DomainCodeMap)
                if (text.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0) return kvp.Value;

            return null;
        }

        public static string? ExtractCleanGrammar(string? grammarText)
        {
            if (string.IsNullOrWhiteSpace(grammarText)) return null;

            var cleaned = CleanText(grammarText.Trim(), true, true);
            if (string.IsNullOrWhiteSpace(cleaned)) return null;

            if (cleaned.StartsWith("PHRASAL VERB", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("PHR V", StringComparison.OrdinalIgnoreCase))
                return "phrasal_verb";

            for (var i = 0; i < cleaned.Length; i++)
            {
                if (!char.IsLetterOrDigit(cleaned[i]) && cleaned[i] != '-' && cleaned[i] != ' ')
                {
                    var result = cleaned.Substring(0, i).Trim();
                    return result.Length <= 100 ? result : null;
                }
            }

            return cleaned.Length <= 100 ? cleaned : null;
        }

        #endregion

        #region Private Helper Methods

        private static CollinsSenseRaw? TryParseEnhancedSenseHeader(string line)
        {
            var match = SenseHeaderEnhancedRegex.Match(line);
            return match.Success ? CreateSenseFromMatch(match) : null;
        }

        private static CollinsSenseRaw? TryParseGrammarCodeWithSlash(string line)
        {
            var match = GrammarCodeWithSlashRegex.Match(line);
            if (match.Success)
            {
                return new CollinsSenseRaw
                {
                    SenseNumber = ExtractSenseNumberFromText(line),
                    PartOfSpeech = FastNormalizePos(match.Groups[1].Value.Trim()),
                    Definition = CleanDefinitionText(match.Groups[2].Value.Trim())
                };
            }
            return null;
        }

        private static CollinsSenseRaw? TryParsePhrasalPattern(string line)
        {
            var match = PhrasalPatternRegex.Match(line);
            if (match.Success)
            {
                // OLD LOGIC (WRONG):
                // match.Groups[1] is NOT POS, regex only has named group definition
                // return new CollinsSenseRaw
                // {
                //     SenseNumber = ExtractSenseNumberFromText(line),
                //     PartOfSpeech = FastNormalizePos(match.Groups[1].Value.Trim()),
                //     Definition = CleanDefinitionText(match.Groups["definition"].Value.Trim())
                // };

                // NEW LOGIC (fixed):
                return new CollinsSenseRaw
                {
                    SenseNumber = ExtractSenseNumberFromText(line),
                    PartOfSpeech = "phrasal_verb",
                    Definition = CleanDefinitionText(match.Groups["definition"].Value.Trim())
                };
            }
            return null;
        }

        private static CollinsSenseRaw? TryParseDoubleSemicolonFormat(string line)
        {
            if (line.Contains('\t') && line.Contains(";;"))
            {
                var parts = line.Split('\t', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var pos = parts[0].Trim();
                    var definition = parts[1].TrimStart(';', ' ');
                    return new CollinsSenseRaw
                    {
                        SenseNumber = ExtractSenseNumberFromText(pos),
                        PartOfSpeech = FastNormalizePos(pos),
                        Definition = CleanDefinitionText(definition)
                    };
                }
            }
            return null;
        }

        private static CollinsSenseRaw? TryParseSimpleNumberedLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            if (line.Contains("【") || line.Contains("】")) return null;

            var match = SenseNumberOnlyRegex.Match(line);
            if (!match.Success) return null;

            var rest = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(rest)) return null;

            if (rest.Length > 0 && !char.IsUpper(rest[0])) return null;

            if (rest.Contains("→see:", StringComparison.OrdinalIgnoreCase)) return null;

            if (rest.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("or ", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("but ", StringComparison.OrdinalIgnoreCase))
                return null;

            return new CollinsSenseRaw
            {
                SenseNumber = int.TryParse(match.Groups[1].Value, out var num) ? num : 1,
                PartOfSpeech = "unk",
                Definition = CleanDefinitionText(rest)
            };
        }

        private static CollinsSenseRaw? CreateSenseFromMatch(Match match)
        {
            if (match == null || !match.Success) return null;

            var posGroup = match.Groups["pos"];
            var definitionGroup = match.Groups["definition"];
            if (!posGroup.Success || !definitionGroup.Success) return null;

            var numberGroup = match.Groups["number"];
            var senseNumber = numberGroup.Success && int.TryParse(numberGroup.Value, out var parsed) ? parsed : 1;

            var pos = posGroup.Value.Trim();
            var definition = definitionGroup.Value.Trim();

            if (string.IsNullOrWhiteSpace(pos) || string.IsNullOrWhiteSpace(definition)) return null;

            // Remove Chinese then clean
            definition = ChineseCharRegex.Replace(definition, "").Trim();
            var cleanedDefinition = CleanDefinitionText(definition);

            if (string.IsNullOrWhiteSpace(cleanedDefinition))
                cleanedDefinition = LenientCleanDefinitionText(definition);

            return new CollinsSenseRaw
            {
                SenseNumber = senseNumber,
                PartOfSpeech = FastNormalizePos(pos),
                Definition = cleanedDefinition
            };
        }

        private static int ExtractSenseNumberFromText(string text)
        {
            var match = Regex.Match(text, @"^(\d+)\.\s*");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
                return number;
            return 1;
        }

        private static string FastNormalizePos(string rawPos)
        {
            if (string.IsNullOrWhiteSpace(rawPos)) return "unk";

            var trimmed = rawPos.Trim();
            trimmed = Regex.Replace(trimmed, @"^\d+\.\s*", "");

            if (PosNormalizationMap.TryGetValue(trimmed, out var exactMatch))
                return exactMatch;

            var noChinese = ChineseCharRegex.Replace(trimmed, "").Trim();
            if (PosNormalizationMap.TryGetValue(noChinese, out var noChineseMatch))
                return noChineseMatch;

            foreach (var prefix in PosPrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var slashIndex = trimmed.IndexOf('/');
                    if (slashIndex > 0)
                    {
                        var firstPart = trimmed.Substring(0, slashIndex).Trim();
                        if (PosNormalizationMap.TryGetValue(firstPart, out var slashMatch))
                            return slashMatch;
                    }

                    if (prefix.StartsWith("N-", StringComparison.OrdinalIgnoreCase) || prefix == "N") return "noun";
                    if (prefix.StartsWith("V-", StringComparison.OrdinalIgnoreCase) || prefix == "V") return "verb";
                    if (prefix.StartsWith("ADJ-", StringComparison.OrdinalIgnoreCase) || prefix == "ADJ") return "adj";
                    if (prefix.StartsWith("ADV-", StringComparison.OrdinalIgnoreCase) || prefix == "ADV") return "adv";
                    if (prefix.StartsWith("PHR-", StringComparison.OrdinalIgnoreCase)) return "phrase";
                }
            }

            if (trimmed.Contains("NOUN", StringComparison.OrdinalIgnoreCase)) return "noun";
            if (trimmed.Contains("VERB", StringComparison.OrdinalIgnoreCase)) return "verb";
            if (trimmed.Contains("ADJECTIVE", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("ADJ", StringComparison.OrdinalIgnoreCase))
                return "adj";
            if (trimmed.Contains("ADVERB", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("ADV", StringComparison.OrdinalIgnoreCase))
                return "adv";

            return "unk";
        }

        private static string LenientCleanDefinitionText(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return definition;

            var result = definition;
            result = ChineseCharRegex.Replace(result, "");
            result = Regex.Replace(result, @"^[\s、。，；,\.\(\)（）:：…\/\\]+", "");
            result = CleanText(result, false, true);
            result = EnsureSentenceStructure(result);

            if (string.IsNullOrWhiteSpace(result))
                result = ChineseCharRegex.Replace(definition, "").Trim();

            return result.Trim();
        }

        private static string EnsureSentenceStructure(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var result = text.Trim();
            if (!string.IsNullOrEmpty(result))
            {
                var lastChar = result[^1];
                if (!(lastChar == '.' || lastChar == '!' || lastChar == '?'))
                {
                    if (result.EndsWith(",") || result.EndsWith(";") || result.EndsWith(":"))
                        result = result.TrimEnd(',', ';', ':') + ".";
                    else
                        result += ".";
                }
            }
            return result;
        }

        #endregion

        #region NEW METHODS (Added - Safe, No Signature Changes)

        // NEW METHOD: safer English extraction when Chinese exists on same line
        private static string ExtractLeadingEnglishSegment(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // if mixed English + Chinese, keep English-only prefix segment before Chinese begins
            var sb = new List<char>(text.Length);

            foreach (var c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                    break;

                sb.Add(c);
            }

            var candidate = new string(sb.ToArray()).Trim();
            candidate = CleanText(candidate, removeChinese: true, normalizePunctuation: true);

            // Avoid returning junk
            if (string.IsNullOrWhiteSpace(candidate))
                return string.Empty;

            return candidate.TrimEnd('.', ',', ';', ':').Trim();
        }

        #endregion

        // NEW METHOD: Builds ParsedDefinition from entry (moves core parser logic here)
        public static ParsedDefinition BuildParsedDefinition(DictionaryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            if (string.IsNullOrWhiteSpace(entry.Definition))
                return BuildFallbackParsedDefinition(entry);

            var definition = entry.Definition;

            var parsedData = ParseCollinsEntry(definition);

            // Build full definition with metadata
            var fullDefinition = BuildFullDefinition(parsedData);

            // Extract synonyms
            var synonyms = ExtractSynonyms(definition, parsedData.Examples);

            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = fullDefinition,
                RawFragment = entry.Definition,
                SenseNumber = parsedData.SenseNumber,
                Domain = parsedData.PrimaryDomain,
                UsageLabel = BuildUsageLabel(parsedData),
                CrossReferences = parsedData.CrossReferences.ToList(),
                Synonyms = synonyms.Count > 0 ? synonyms : null,
                Alias = parsedData.PhrasalVerbInfo.IsPhrasalVerb
                       ? $"{parsedData.PhrasalVerbInfo.Verb} {parsedData.PhrasalVerbInfo.Particle}"
                       : null,
                Examples = parsedData.Examples.ToList()
            };
        }

        // NEW METHOD: Exact old BuildFullDefinition moved here
        public static string BuildFullDefinition(CollinsParsedData data)
        {
            var parts = new List<string>();

            // Add main definition
            if (!string.IsNullOrWhiteSpace(data.CleanDefinition))
                parts.Add(data.CleanDefinition);
            else if (!string.IsNullOrWhiteSpace(data.MainDefinition))
                parts.Add(data.MainDefinition);

            // Add POS if available
            if (!string.IsNullOrWhiteSpace(data.PartOfSpeech) && data.PartOfSpeech != "unk")
                parts.Insert(0, $"【POS】{data.PartOfSpeech}");

            // Add domain labels
            if (data.DomainLabels.Count > 0)
                parts.Add($"【Domains】{string.Join(", ", data.DomainLabels)}");

            // Add usage patterns
            if (data.UsagePatterns.Count > 0)
                parts.Add($"【Patterns】{string.Join("; ", data.UsagePatterns)}");

            // Add phrasal verb info
            if (data.PhrasalVerbInfo.IsPhrasalVerb)
            {
                parts.Add($"【PhrasalVerb】{data.PhrasalVerbInfo.Verb} {data.PhrasalVerbInfo.Particle}");
                if (data.PhrasalVerbInfo.Patterns.Count > 0)
                    parts.Add($"【PhrasalPatterns】{string.Join("; ", data.PhrasalVerbInfo.Patterns)}");
            }

            return string.Join("\n", parts).Trim();
        }

        // NEW METHOD: Exact old BuildUsageLabel moved here
        public static string BuildUsageLabel(CollinsParsedData data)
        {
            var labels = new List<string>();

            if (!string.IsNullOrWhiteSpace(data.PartOfSpeech) && data.PartOfSpeech != "unk")
                labels.Add(data.PartOfSpeech);

            if (data.DomainLabels.Count > 0)
                labels.AddRange(data.DomainLabels.Take(2));

            return labels.Count > 0 ? string.Join(", ", labels) : null;
        }

        // NEW METHOD: fallback builder moved here for reuse
        public static ParsedDefinition BuildFallbackParsedDefinition(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = entry.Definition ?? string.Empty,
                RawFragment = entry.Definition ?? string.Empty,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }

    }

    /// <summary>
    /// Structured data container for Collins parsing results
    /// </summary>
    public class CollinsParsedData
    {
        public int SenseNumber { get; set; } = 1;
        public string PartOfSpeech { get; set; } = "unk";
        public string ChinesePartOfSpeech { get; set; } = string.Empty;
        public string MainDefinition { get; set; } = string.Empty;
        public string RawDefinitionStart { get; set; } = string.Empty;
        public string CleanDefinition { get; set; } = string.Empty;
        public IReadOnlyList<string> DomainLabels { get; set; } = new List<string>();
        public IReadOnlyList<string> UsagePatterns { get; set; } = new List<string>();
        public IReadOnlyList<string> Examples { get; set; } = new List<string>();
        public IReadOnlyList<CrossReference> CrossReferences { get; set; } = new List<CrossReference>();
        public PhrasalVerbInfo PhrasalVerbInfo { get; set; } = new PhrasalVerbInfo();
        public bool IsPhrasalVerb { get; set; }
        public string PrimaryDomain => DomainLabels?.FirstOrDefault() ?? string.Empty;
        public string PrimaryUsagePattern => UsagePatterns?.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Information about phrasal verbs
    /// </summary>
    public class PhrasalVerbInfo
    {
        public bool IsPhrasalVerb { get; set; }
        public string Verb { get; set; } = string.Empty;
        public string Particle { get; set; } = string.Empty;
        public IReadOnlyList<string> Patterns { get; set; } = new List<string>();
    }

    // NOTE: CrossReference and CollinsSenseRaw classes are referenced by your existing project.
    // Keep them as-is in your codebase.
}
