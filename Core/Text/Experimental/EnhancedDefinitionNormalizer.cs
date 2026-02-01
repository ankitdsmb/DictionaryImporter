namespace DictionaryImporter.Core.Text.Experimental
{
    public sealed class EnhancedDefinitionNormalizer : IDefinitionNormalizer
    {
        #region Configuration

        public class NormalizationOptions
        {
            public bool RemoveExamples { get; set; } = false;
            public bool RemoveSynonyms { get; set; } = false;
            public bool RemoveAntonyms { get; set; } = false;
            public bool ExpandAbbreviations { get; set; } = true;
            public bool NormalizePartsOfSpeech { get; set; } = true;
            public bool PreserveMetadata { get; set; } = false;
            public bool DeduplicateSenses { get; set; } = true;
            public bool SortByLength { get; set; } = false;
            public bool ForceNumbering { get; set; } = false;
            public int MaxDefinitionLength { get; set; } = 500;
            public double SimilarityThreshold { get; set; } = 0.85;
            public string OutputFormat { get; set; } = "numbered"; // numbered, bullet, compact, paragraph
        }

        private static readonly Regex[] SenseSplitters =
        {
            new Regex(@"(?<=\n)\s*\d+[\.\)]\s*", RegexOptions.Compiled),
            new Regex(@"(?<=\n)\s*[a-z][\.\)]\s*", RegexOptions.Compiled),
            new Regex(@"(?<=\n)\s*[•\-]\s*", RegexOptions.Compiled),
            new Regex(@"(?<=\n)\s*\*\s*", RegexOptions.Compiled),
            new Regex(@";(?=\s+[A-Z])", RegexOptions.Compiled), // Semicolon before capital
        };

        private static readonly Regex[] RemovePatterns =
        {
            new Regex(@"^\s*\d+[\.\)]\s*", RegexOptions.Compiled),
            new Regex(@"^\s*[a-z][\.\)]\s*", RegexOptions.Compiled),
            new Regex(@"^\s*[•\-\*]\s*", RegexOptions.Compiled),
            new Regex(@"\s*\[[^\]]*\]\s*", RegexOptions.Compiled),
            new Regex(@"\s*\([^)]*\)\s*", RegexOptions.Compiled),
        };

        private static readonly Dictionary<string, string> CommonAbbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cf."] = "compare",
            ["i.e."] = "that is",
            ["e.g."] = "for example",
            ["etc."] = "and so on",
            ["viz."] = "namely",
            ["approx."] = "approximately",
            ["esp."] = "especially",
            ["usu."] = "usually",
            ["arch."] = "archaic",
            ["obs."] = "obsolete",
            ["lit."] = "literally",
            ["fig."] = "figuratively",
            ["syn."] = "synonym",
            ["ant."] = "antonym",
            ["pl."] = "plural",
            ["sing."] = "singular",
        };

        private static readonly Dictionary<string, string> PartsOfSpeech = new(StringComparer.OrdinalIgnoreCase)
        {
            ["noun"] = "n.",
            ["substantive"] = "n.",
            ["verb"] = "v.",
            ["verbal"] = "v.",
            ["adjective"] = "adj.",
            ["adjectival"] = "adj.",
            ["adverb"] = "adv.",
            ["adverbial"] = "adv.",
            ["preposition"] = "prep.",
            ["conjunction"] = "conj.",
            ["interjection"] = "interj.",
            ["pronoun"] = "pron.",
        };

        private readonly NormalizationOptions _options;

        #endregion Configuration

        #region Constructor

        public EnhancedDefinitionNormalizer(NormalizationOptions options = null)
        {
            _options = options ?? new NormalizationOptions();
        }

        #endregion Constructor

        #region Public Interface

        /// <summary>
        /// Normalizes a dictionary definition with comprehensive processing
        /// </summary>
        public string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            try
            {
                // Preserve original for fallback
                string original = raw;

                // Basic preprocessing
                raw = PreprocessText(raw);

                // Extract metadata if needed
                Dictionary<string, string> metadata = new();
                if (_options.PreserveMetadata)
                {
                    raw = ExtractMetadata(raw, out metadata);
                }

                // Try to extract senses
                var senses = ExtractSenses(raw);

                // If no senses found, try alternative methods
                if (senses.Count == 0)
                {
                    // Try to split by common separators
                    senses = SplitByCommonSeparators(raw);
                }

                // If still no senses, treat as single definition
                if (senses.Count == 0)
                {
                    senses.Add(raw);
                }

                // Clean each sense
                senses = CleanSenses(senses);

                // Apply transformations based on options
                if (_options.RemoveExamples)
                    senses = RemoveExamples(senses);

                if (_options.RemoveSynonyms)
                    senses = RemoveSynonyms(senses);

                if (_options.RemoveAntonyms)
                    senses = RemoveAntonyms(senses);

                if (_options.ExpandAbbreviations)
                    senses = ExpandAbbreviations(senses);

                if (_options.NormalizePartsOfSpeech)
                    senses = NormalizePartsOfSpeech(senses);

                if (_options.DeduplicateSenses)
                    senses = DeduplicateSenses(senses);

                if (_options.SortByLength)
                    senses = SortSensesByLength(senses);

                // Format output
                string result = FormatOutput(senses, metadata);

                // Return result or fallback to cleaned original
                return !string.IsNullOrWhiteSpace(result) ? result : CleanSingleDefinition(original);
            }
            catch
            {
                // Fallback to simple normalization
                return CleanSingleDefinition(raw);
            }
        }

        /// <summary>
        /// Normalizes definition for a specific dictionary source
        /// </summary>
        public string NormalizeForSource(string raw, string sourceType)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            // Apply source-specific preprocessing
            raw = sourceType?.ToLower() switch
            {
                "oxford" => PreprocessOxford(raw),
                "collins" => PreprocessCollins(raw),
                "gutenbergwebster" => PreprocessGutenbergWebster(raw),
                "englishchinese" => PreprocessEnglishChinese(raw),
                "century21" => PreprocessCentury21(raw),
                "kaikki" => PreprocessKaikki(raw),
                "structuredjson" => PreprocessStructuredJson(raw),
                _ => raw
            };

            return Normalize(raw);
        }

        /// <summary>
        /// Analyzes a definition and returns statistics
        /// </summary>
        public DefinitionAnalysis Analyze(string raw)
        {
            var analysis = new DefinitionAnalysis
            {
                Original = raw,
                Normalized = Normalize(raw)
            };

            if (!string.IsNullOrWhiteSpace(raw))
            {
                analysis.SenseCount = CountSenses(raw);
                analysis.ContainsExamples = ContainsExamples(raw);
                analysis.ContainsSynonyms = ContainsSynonyms(raw);
                analysis.ContainsAntonyms = ContainsAntonyms(raw);
                analysis.PartOfSpeech = DetectPartOfSpeech(raw);
                analysis.WordCount = CountWords(raw);
                analysis.HasMultilingual = HasMultilingualContent(raw);
                analysis.ReadabilityScore = CalculateReadabilityScore(raw);
            }

            return analysis;
        }

        /// <summary>
        /// Parses a definition into structured senses
        /// </summary>
        public List<DefinitionSense> ParseToSenses(string raw)
        {
            var normalized = Normalize(raw);
            var lines = normalized.Split('\n');
            var senses = new List<DefinitionSense>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var sense = new DefinitionSense
                {
                    Number = i + 1,
                    Text = ExtractSenseText(line),
                    PartOfSpeech = DetectPartOfSpeech(line),
                    HasExample = ContainsExamples(line),
                    HasSynonyms = ContainsSynonyms(line),
                    HasAntonyms = ContainsAntonyms(line),
                    WordCount = CountWords(line)
                };

                senses.Add(sense);
            }

            return senses;
        }

        #endregion Public Interface

        #region Core Processing Methods

        private string PreprocessText(string text)
        {
            // Normalize line endings
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // Normalize whitespace
            text = Regex.Replace(text, @"[ \t]+", " ");

            // Normalize quotes
            text = text.Replace("“", "\"").Replace("”", "\"")
                      .Replace("‘", "'").Replace("’", "'");

            // Fix spacing around punctuation
            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");
            text = Regex.Replace(text, @"([(\[])\s+", "$1");
            text = Regex.Replace(text, @"\s+([)\]])", "$1");

            // Remove excessive punctuation
            text = Regex.Replace(text, @";\s*;", ";");
            text = Regex.Replace(text, @"\.\s*\.", ".");
            text = Regex.Replace(text, @",\s*,", ",");

            return text.Trim();
        }

        private string ExtractMetadata(string text, out Dictionary<string, string> metadata)
        {
            metadata = new Dictionary<string, string>();

            // Extract pronunciation
            var pronMatch = Regex.Match(text, @"/[^/]+/");
            if (pronMatch.Success)
            {
                metadata["pronunciation"] = pronMatch.Value;
                text = text.Replace(pronMatch.Value, "").Trim();
            }

            // Extract etymology
            var etymMatch = Regex.Match(text, @"\[[^\]]*\]");
            if (etymMatch.Success && etymMatch.Value.Length < 100)
            {
                metadata["etymology"] = etymMatch.Value.Trim('[', ']');
                text = text.Replace(etymMatch.Value, "").Trim();
            }

            return text;
        }

        private List<string> ExtractSenses(string text)
        {
            var senses = new List<string>();

            // Check for obvious multi-sense patterns
            foreach (var splitter in SenseSplitters)
            {
                if (splitter.IsMatch(text))
                {
                    var parts = splitter.Split(text);
                    if (parts.Length > 1)
                    {
                        // First part might be preamble
                        bool hasPreamble = parts[0].Trim().Length < 50 &&
                                          (parts[0].Contains('.') || parts[0].Length < 20);

                        for (int i = hasPreamble ? 1 : 0; i < parts.Length; i++)
                        {
                            var part = parts[i].Trim();
                            if (!string.IsNullOrWhiteSpace(part) && part.Length > 2)
                            {
                                senses.Add(part);
                            }
                        }
                        break;
                    }
                }
            }

            // If no patterns found, try splitting by semicolons for longer texts
            if (senses.Count == 0 && text.Length > 100 && text.Contains(';'))
            {
                var parts = text.Split(';')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p) && p.Length > 10)
                    .ToList();

                if (parts.Count > 1)
                {
                    senses.AddRange(parts);
                }
            }

            return senses;
        }

        private List<string> SplitByCommonSeparators(string text)
        {
            var separators = new[] { ";", ":", "|", "/" };
            var senses = new List<string>();

            foreach (var separator in separators)
            {
                if (text.Contains(separator))
                {
                    var parts = text.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();

                    if (parts.Count > 1)
                    {
                        // Check if these look like separate senses
                        int capitalStarts = parts.Count(p => p.Length > 0 && char.IsUpper(p[0]));
                        if (capitalStarts >= parts.Count / 2)
                        {
                            senses.AddRange(parts);
                            break;
                        }
                    }
                }
            }

            return senses;
        }

        private List<string> CleanSenses(List<string> senses)
        {
            return senses.Select(CleanSense).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private string CleanSense(string sense)
        {
            // Remove common patterns
            foreach (var pattern in RemovePatterns)
            {
                sense = pattern.Replace(sense, " ");
            }

            // Remove duplicate words
            sense = RemoveDuplicateWords(sense);

            // Clean punctuation
            sense = Regex.Replace(sense, @"\s+([,.;:!?])", "$1");
            sense = Regex.Replace(sense, @"([,.;:])\s*$", ".");
            sense = Regex.Replace(sense, @"\s+", " ");

            // Capitalize and punctuate
            sense = sense.Trim();
            if (sense.Length > 0)
            {
                if (char.IsLower(sense[0]))
                {
                    sense = char.ToUpper(sense[0]) + sense.Substring(1);
                }

                if (!".!?:;".Contains(sense[^1]))
                {
                    sense += ".";
                }
            }

            return sense;
        }

        private string CleanSingleDefinition(string text)
        {
            text = PreprocessText(text);

            // Remove common markers
            text = Regex.Replace(text, @"^\s*(n\.|v\.|adj\.|adv\.|prep\.|conj\.|interj\.|pron\.)\s*", "",
                RegexOptions.IgnoreCase);

            // Remove examples in parentheses
            text = Regex.Replace(text, @"\s*\([^)]*\)\s*", " ");

            // Normalize
            text = Regex.Replace(text, @";\s*", "; ");
            text = text.Trim();

            if (text.Length > 0)
            {
                if (char.IsLower(text[0]))
                    text = char.ToUpper(text[0]) + text.Substring(1);

                if (!".!?:;".Contains(text[^1]))
                    text += ".";
            }

            return text;
        }

        private List<string> RemoveExamples(List<string> senses)
        {
            return senses.Select(sense =>
            {
                var patterns = new[]
                {
                    @"\s+e\.g\.[^.]*\.?",
                    @"\s+for example[^.]*\.?",
                    @"\s+such as[^.]*\.?",
                    @"\s+including[^.]*\.?"
                };

                foreach (var pattern in patterns)
                    sense = Regex.Replace(sense, pattern, "", RegexOptions.IgnoreCase);

                return sense.Trim();
            }).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private List<string> RemoveSynonyms(List<string> senses)
        {
            return senses.Select(sense =>
            {
                var patterns = new[]
                {
                    @"\s+syn\.[^.]*\.?",
                    @"\s+synonyms?:[^.]*\.?",
                    @"\s+also called[^.]*\.?",
                    @"\s+same as[^.]*\.?"
                };

                foreach (var pattern in patterns)
                    sense = Regex.Replace(sense, pattern, "", RegexOptions.IgnoreCase);

                return sense.Trim();
            }).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private List<string> RemoveAntonyms(List<string> senses)
        {
            return senses.Select(sense =>
            {
                var patterns = new[]
                {
                    @"\s+ant\.[^.]*\.?",
                    @"\s+antonyms?:[^.]*\.?",
                    @"\s+opposite of[^.]*\.?",
                    @"\s+contrasted with[^.]*\.?"
                };

                foreach (var pattern in patterns)
                    sense = Regex.Replace(sense, pattern, "", RegexOptions.IgnoreCase);

                return sense.Trim();
            }).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private List<string> ExpandAbbreviations(List<string> senses)
        {
            return senses.Select(sense =>
            {
                foreach (var abbr in CommonAbbreviations.OrderByDescending(a => a.Key.Length))
                {
                    var pattern = $@"\b{Regex.Escape(abbr.Key)}\b";
                    sense = Regex.Replace(sense, pattern, abbr.Value, RegexOptions.IgnoreCase);
                }
                return sense;
            }).ToList();
        }

        private List<string> NormalizePartsOfSpeech(List<string> senses)
        {
            return senses.Select(sense =>
            {
                foreach (var pos in PartsOfSpeech)
                {
                    var pattern = $@"\b{Regex.Escape(pos.Key)}\b";
                    sense = Regex.Replace(sense, pattern, pos.Value, RegexOptions.IgnoreCase);
                }
                return sense;
            }).ToList();
        }

        private List<string> DeduplicateSenses(List<string> senses)
        {
            if (senses.Count <= 1)
                return senses;

            var unique = new List<string>();
            foreach (var sense in senses)
            {
                bool isDuplicate = false;
                foreach (var uniqueSense in unique)
                {
                    double similarity = CalculateSimilarity(sense, uniqueSense);
                    if (similarity >= _options.SimilarityThreshold)
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                    unique.Add(sense);
            }

            return unique;
        }

        private List<string> SortSensesByLength(List<string> senses)
        {
            return senses.OrderBy(s => s.Length).ToList();
        }

        private string FormatOutput(List<string> senses, Dictionary<string, string> metadata)
        {
            if (!senses.Any())
                return string.Empty;

            var output = new StringBuilder();

            // Add metadata if present
            if (metadata.Any())
            {
                output.AppendLine("[");
                foreach (var kv in metadata)
                {
                    output.AppendLine($"  {kv.Key}: {kv.Value}");
                }
                output.Append("]");
                if (senses.Any())
                    output.AppendLine();
            }

            // Format senses
            switch (_options.OutputFormat.ToLower())
            {
                case "numbered":
                    for (int i = 0; i < senses.Count; i++)
                        output.AppendLine($"{i + 1}. {senses[i]}");
                    break;

                case "bullet":
                    foreach (var sense in senses)
                        output.AppendLine($"• {sense}");
                    break;

                case "compact":
                    output.Append(string.Join("; ", senses));
                    break;

                case "paragraph":
                    output.Append(string.Join(" ", senses));
                    break;

                default:
                    for (int i = 0; i < senses.Count; i++)
                        output.AppendLine($"{i + 1}. {senses[i]}");
                    break;
            }

            return output.ToString().Trim();
        }

        #endregion Core Processing Methods

        #region Source-Specific Preprocessing

        private string PreprocessOxford(string text)
        {
            text = Regex.Replace(text, @"\b(Oxford|OED|OE|ME)\b\.?\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\[.*?\]", " ");
            return text;
        }

        private string PreprocessCollins(string text)
        {
            text = Regex.Replace(text, @"(?<=\b\w)\/(?=\w\b)", " or ");
            text = Regex.Replace(text, @"\bCOBUILD\b\s*", "", RegexOptions.IgnoreCase);
            return text;
        }

        private string PreprocessGutenbergWebster(string text)
        {
            text = Regex.Replace(text, @"\bWebster\b[''s]*\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(Def|Defn|Definition)\s*\d*[\.:]?\s*", "", RegexOptions.IgnoreCase);
            return text;
        }

        private string PreprocessEnglishChinese(string text)
        {
            text = Regex.Replace(text, @"【.*?】", " ");
            text = Regex.Replace(text, @"\/\s*", "; ");
            return text;
        }

        private string PreprocessCentury21(string text)
        {
            text = Regex.Replace(text, @"\bC21\b\s*", "", RegexOptions.IgnoreCase);
            return text;
        }

        private string PreprocessKaikki(string text)
        {
            text = Regex.Replace(text, @"\{\{.*?\}\}", " ");
            text = Regex.Replace(text, @"\[\[.*?\]\]", " ");
            text = Regex.Replace(text, @"'''", "");
            text = Regex.Replace(text, @"''", "");
            return text;
        }

        private string PreprocessStructuredJson(string text)
        {
            // Clean JSON-like structures
            text = Regex.Replace(text, @"""([^""]*)"":\s*""([^""]*)""", "$2");
            text = Regex.Replace(text, @"\{.*?\}", " ");
            text = Regex.Replace(text, @"\[.*?\]", " ");
            return text;
        }

        #endregion Source-Specific Preprocessing

        #region Analysis Methods

        private int CountSenses(string text)
        {
            var patterns = new[]
            {
                @"\n\s*\d+[\.\)]",
                @"\n\s*[a-z][\.\)]",
                @"\n\s*•",
                @";\s*(?=[A-Z])"
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(text, pattern);
                if (matches.Count > 0)
                    return matches.Count + 1;
            }

            return 1;
        }

        private bool ContainsExamples(string text)
        {
            return Regex.IsMatch(text, @"\b(e\.g\.|for example|such as|including)\b",
                RegexOptions.IgnoreCase);
        }

        private bool ContainsSynonyms(string text)
        {
            return Regex.IsMatch(text, @"\b(syn\.|synonyms?|also called|same as)\b",
                RegexOptions.IgnoreCase);
        }

        private bool ContainsAntonyms(string text)
        {
            return Regex.IsMatch(text, @"\b(ant\.|antonyms?|opposite of|contrasted with)\b",
                RegexOptions.IgnoreCase);
        }

        private string DetectPartOfSpeech(string text)
        {
            var patterns = new Dictionary<string, string>
            {
                [@"\b(n\.|noun|substantive)\b"] = "Noun",
                [@"\b(v\.|verb|verbal)\b"] = "Verb",
                [@"\b(adj\.|adjective|adjectival)\b"] = "Adjective",
                [@"\b(adv\.|adverb|adverbial)\b"] = "Adverb",
                [@"\b(prep\.|preposition)\b"] = "Preposition",
                [@"\b(conj\.|conjunction)\b"] = "Conjunction",
                [@"\b(interj\.|interjection)\b"] = "Interjection",
                [@"\b(pron\.|pronoun)\b"] = "Pronoun"
            };

            foreach (var pattern in patterns)
            {
                if (Regex.IsMatch(text, pattern.Key, RegexOptions.IgnoreCase))
                    return pattern.Value;
            }

            return "Unknown";
        }

        private int CountWords(string text)
        {
            return Regex.Matches(text, @"\b\w+\b").Count;
        }

        private bool HasMultilingualContent(string text)
        {
            return Regex.IsMatch(text, @"[\u4e00-\u9fff]") || // Chinese
                   Regex.IsMatch(text, @"[\u0400-\u04FF]") || // Cyrillic
                   Regex.IsMatch(text, @"[\u0600-\u06FF]");   // Arabic
        }

        private double CalculateReadabilityScore(string text)
        {
            var words = Regex.Matches(text, @"\b\w+\b")
                .Cast<Match>()
                .Select(m => m.Value)
                .ToList();

            var sentences = text.Split('.', '!', '?')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (!words.Any() || !sentences.Any())
                return 100;

            double wordsPerSentence = words.Count / (double)sentences.Count;
            double syllablesPerWord = words.Sum(w => CountSyllables(w)) / (double)words.Count;

            // Flesch Reading Ease
            return Math.Max(0, Math.Min(100, 206.835 - 1.015 * wordsPerSentence - 84.6 * syllablesPerWord));
        }

        private double CalculateSimilarity(string a, string b)
        {
            // Simple similarity calculation
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return 1.0;

            var wordsA = a.Split(' ').Distinct().ToArray();
            var wordsB = b.Split(' ').Distinct().ToArray();

            int common = wordsA.Intersect(wordsB, StringComparer.OrdinalIgnoreCase).Count();
            int total = wordsA.Union(wordsB, StringComparer.OrdinalIgnoreCase).Count();

            return total > 0 ? (double)common / total : 0;
        }

        #endregion Analysis Methods

        #region Helper Methods

        private string RemoveDuplicateWords(string text)
        {
            var words = text.Split(' ');
            var result = new StringBuilder();
            var lastWord = "";

            foreach (var word in words)
            {
                var cleanWord = Regex.Replace(word, @"[^\w']", "").ToLower();
                if (cleanWord != lastWord || cleanWord.Length > 5)
                {
                    result.Append(word);
                    result.Append(' ');
                    lastWord = cleanWord;
                }
            }

            return result.ToString().Trim();
        }

        private int CountSyllables(string word)
        {
            word = word.ToLower().Trim();
            if (word.Length <= 3)
                return 1;

            word = Regex.Replace(word, @"[^aeiouy]", "");
            word = Regex.Replace(word, @"[aeiouy]{2,}", "a");

            return Math.Max(1, word.Length);
        }

        private string ExtractSenseText(string line)
        {
            // Remove numbering
            line = Regex.Replace(line, @"^\s*\d+[\.\)]\s*", "");
            line = Regex.Replace(line, @"^\s*[•\-\*]\s*", "");
            return line.Trim();
        }

        #endregion Helper Methods

        #region Supporting Classes

        public class DefinitionAnalysis
        {
            public string Original { get; set; }
            public string Normalized { get; set; }
            public int SenseCount { get; set; }
            public bool ContainsExamples { get; set; }
            public bool ContainsSynonyms { get; set; }
            public bool ContainsAntonyms { get; set; }
            public string PartOfSpeech { get; set; }
            public int WordCount { get; set; }
            public bool HasMultilingual { get; set; }
            public double ReadabilityScore { get; set; }

            public override string ToString()
            {
                return $"Senses: {SenseCount}, POS: {PartOfSpeech}, Words: {WordCount}, Readability: {ReadabilityScore:F1}";
            }
        }

        public class DefinitionSense
        {
            public int Number { get; set; }
            public string Text { get; set; }
            public string PartOfSpeech { get; set; }
            public bool HasExample { get; set; }
            public bool HasSynonyms { get; set; }
            public bool HasAntonyms { get; set; }
            public int WordCount { get; set; }

            public override string ToString()
            {
                return $"{Number}. {Text}";
            }
        }

        #endregion Supporting Classes
    }
}