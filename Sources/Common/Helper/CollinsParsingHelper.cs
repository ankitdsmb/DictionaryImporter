namespace DictionaryImporter.Sources.Common.Helper
{
    internal static class CollinsParsingHelper
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

            // Clean and normalize
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
        /// Extract the main English definition text
        /// </summary>
        public static string ExtractMainDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            // Find the end of the sense header and start of actual definition
            var lines = definition.Split('\n');
            var inDefinition = false;
            var definitionLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Skip empty lines
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Start collecting after sense header
                if (!inDefinition && (trimmed.StartsWith("1.") || trimmed.StartsWith("2.") ||
                    trimmed.StartsWith("3.") || trimmed.StartsWith("4.") || trimmed.StartsWith("5.")))
                {
                    inDefinition = true;
                    // Remove the numbered prefix
                    var afterNumber = Regex.Replace(trimmed, @"^\d+\.\s*", "");
                    definitionLines.Add(afterNumber);
                    continue;
                }

                if (inDefinition)
                {
                    // Stop when we hit a section marker or next sense
                    if (trimmed.StartsWith("【") ||
                        Regex.IsMatch(trimmed, @"^\d+\.\s*[A-Z]") ||
                        trimmed.StartsWith("..."))
                    {
                        break;
                    }

                    definitionLines.Add(trimmed);
                }
            }

            var rawDefinition = string.Join(" ", definitionLines).Trim();
            return CleanDefinitionText(rawDefinition);
        }

        /// <summary>
        /// Extract domain/register labels (语域标签)
        /// </summary>
        public static IReadOnlyList<string> ExtractDomainLabels(string definition)
        {
            var labels = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return labels;

            // Pattern for 【语域标签】：LABEL1  LABEL2
            var labelMatches = Regex.Matches(definition,
                @"【语域标签】：\s*(?<labels>[^【\n]+)");

            foreach (Match match in labelMatches)
            {
                var labelText = match.Groups["labels"].Value.Trim();
                // Split by space, comma, or Chinese space
                var splitLabels = Regex.Split(labelText, @"[\s,，]+");

                foreach (var label in splitLabels)
                {
                    var cleanLabel = label.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanLabel))
                    {
                        labels.Add(cleanLabel);
                    }
                }
            }

            // Also check for informal/formal markers in the definition
            if (definition.Contains("INFORMAL") && !labels.Any(l => l.Contains("INFORMAL")))
                labels.Add("INFORMAL");

            if (definition.Contains("FORMAL") && !labels.Any(l => l.Contains("FORMAL")))
                labels.Add("FORMAL");

            if (definition.Contains("主美") && !labels.Any(l => l.Contains("US")))
                labels.Add("US");

            if (definition.Contains("主英") && !labels.Any(l => l.Contains("UK")))
                labels.Add("UK");

            return labels.Distinct().ToList();
        }

        /// <summary>
        /// Extract usage patterns (搭配模式)
        /// </summary>
        public static IReadOnlyList<string> ExtractUsagePatterns(string definition)
        {
            var patterns = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return patterns;

            // Pattern for 【搭配模式】：PATTERN
            var patternMatches = Regex.Matches(definition,
                @"【搭配模式】：\s*(?<pattern>[^【\n]+)");

            foreach (Match match in patternMatches)
            {
                var pattern = match.Groups["pattern"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    patterns.Add(pattern);
                }
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
            var inExamples = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Start of examples section
                if (trimmed.StartsWith("..."))
                {
                    inExamples = true;
                    var example = trimmed.Substring(3).Trim();
                    if (!string.IsNullOrWhiteSpace(example))
                    {
                        examples.Add(CleanExampleText(example));
                    }
                    continue;
                }

                // Additional example lines
                if (inExamples)
                {
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("【") ||
                        Regex.IsMatch(trimmed, @"^\d+\."))
                    {
                        inExamples = false;
                        continue;
                    }

                    // Handle Chinese example format: English text。Chinese translation。
                    if (trimmed.Contains("。") && !trimmed.StartsWith("【"))
                    {
                        var example = trimmed.Split('。')[0].Trim();
                        if (!string.IsNullOrWhiteSpace(example) && example.Length > 10)
                        {
                            examples.Add(CleanExampleText(example));
                        }
                    }
                    else if (trimmed.Length > 20 && char.IsUpper(trimmed[0]) &&
                            !trimmed.StartsWith("【") && !Regex.IsMatch(trimmed, @"^\d+\."))
                    {
                        // Likely an English example
                        examples.Add(CleanExampleText(trimmed));
                    }
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

            // Pattern for →see: word
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

            // Also check for "See also:" patterns
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
                        if (!string.IsNullOrWhiteSpace(cleanWord) && cleanWord.Length > 2)
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

            if (string.IsNullOrWhiteSpace(definition) || !definition.Contains("PHR-V"))
                return info;

            // Check if this is a phrasal verb
            if (Regex.IsMatch(definition, @"\bPHR-V\b") ||
                definition.Contains("短语动词") ||
                definition.Contains("PHRASAL VERB"))
            {
                info.IsPhrasalVerb = true;

                // Extract particle
                var particleMatch = Regex.Match(definition,
                    @"\b(PHR-V|PHRASAL VERB)\s+(?<verb>\w+)\s+(?<particle>\w+)\b",
                    RegexOptions.IgnoreCase);

                if (particleMatch.Success)
                {
                    info.Verb = particleMatch.Groups["verb"].Value;
                    info.Particle = particleMatch.Groups["particle"].Value;
                }

                // Extract patterns
                info.Patterns = ExtractUsagePatterns(definition);
            }

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

            // Pattern 1: "also called X" or "also known as X"
            var alsoCalledMatches = Regex.Matches(definition,
                @"(?:also called|also known as|synonymous with|same as)\s+(?<word>\b[A-Z][a-z]+\b)",
                RegexOptions.IgnoreCase);

            foreach (Match match in alsoCalledMatches)
            {
                var synonym = match.Groups["word"].Value.ToLowerInvariant();
                if (synonym.Length > 2)
                    synonyms.Add(synonym);
            }

            // Pattern 2: Parenthetical synonyms "(also X)"
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

            // Pattern 3: From examples
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

        /// <summary>
        /// Extract synonyms from a single example sentence
        /// </summary>
        private static IReadOnlyList<string> ExtractSynonymsFromExample(string example)
        {
            var synonyms = new List<string>();

            if (string.IsNullOrWhiteSpace(example))
                return synonyms;

            // Look for synonym patterns in examples
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
        /// Clean Collins definition text
        /// </summary>
        public static string CleanCollinsDefinition(string definition, CollinsParsedData data = null)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var cleaned = definition;

            // Remove Collins star markers
            cleaned = Regex.Replace(cleaned, @"^★+☆+\s*", "");

            // Remove sense number and POS header
            cleaned = Regex.Replace(cleaned, @"^\d+\.[A-Z\-]+\t[^\t]+\s+", "");

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

            // Remove Chinese translations (text after 。 that contains Chinese)
            var lines = cleaned.Split('\n');
            var englishLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                // Check if line contains Chinese characters
                if (ContainsChineseCharacters(trimmed))
                {
                    // Keep only English part before Chinese punctuation
                    var englishPart = trimmed.Split('。', '，', '；')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(englishPart))
                        englishLines.Add(englishPart);
                }
                else
                {
                    // Keep English line
                    englishLines.Add(trimmed);
                }
            }

            cleaned = string.Join(" ", englishLines);

            // Final cleanup
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            cleaned = cleaned.TrimEnd('.', ',', ';', ':');

            // Ensure proper sentence structure
            if (!string.IsNullOrEmpty(cleaned) && !cleaned.EndsWith("."))
                cleaned += ".";

            return cleaned;
        }

        /// <summary>
        /// Clean example text
        /// </summary>
        private static string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return example;

            var cleaned = example.Trim();

            // Remove leading ellipsis
            if (cleaned.StartsWith("..."))
                cleaned = cleaned.Substring(3).Trim();

            // Remove Chinese translation
            if (cleaned.Contains("。"))
                cleaned = cleaned.Split('。')[0].Trim();

            // Ensure proper punctuation
            if (!string.IsNullOrEmpty(cleaned))
            {
                if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
                    cleaned += ".";

                // Ensure first letter is capitalized
                if (cleaned.Length > 0 && char.IsLower(cleaned[0]))
                    cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
            }

            return cleaned;
        }

        /// <summary>
        /// Clean definition text (remove formatting, normalize)
        /// </summary>
        private static string CleanDefinitionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var cleaned = text;

            // Remove cross-reference markers
            cleaned = Regex.Replace(cleaned, @"→see:\s*\w+", "");

            // Remove example markers
            cleaned = Regex.Replace(cleaned, @"^\.\.\.\s*", "", RegexOptions.Multiline);

            // Remove Chinese text
            cleaned = RemoveChineseText(cleaned);

            // Remove extra whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        /// <summary>
        /// Normalize Collins POS codes
        /// </summary>
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

            var index = text.IndexOf(sectionMarker);
            if (index < 0)
                return text;

            // Find the end of this section
            var afterMarker = text.Substring(index + sectionMarker.Length);
            var endIndex = afterMarker.IndexOf("【");

            if (endIndex >= 0)
            {
                return text.Substring(0, index) + afterMarker.Substring(endIndex);
            }

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

            // Remove Chinese punctuation markers
            cleaned = cleaned.Replace("【", "").Replace("】", "")
                           .Replace("〈", "").Replace("〉", "");

            return Regex.Replace(cleaned, @"\s+", " ").Trim();
        }

        #endregion
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
}