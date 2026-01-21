namespace DictionaryImporter.Sources.Common.Helper
{
    internal static class EnglishChineseParsingHelper
    {
        // MAIN PARSING METHOD
        public static EnglishChineseParsedData ParseEngChnEntry(string rawLine)
        {
            var data = new EnglishChineseParsedData();

            if (string.IsNullOrWhiteSpace(rawLine))
                return data;

            // Split at ⬄ separator (English ⬄ Chinese)
            var parts = rawLine.Split('⬄', 2);
            if (parts.Length != 2)
            {
                // No separator, use entire line
                data.EnglishDefinition = rawLine.Trim();
                return data;
            }

            var englishSide = parts[0].Trim();
            var chineseSide = parts[1].Trim();

            // Extract from English side (headword, IPA, etc.)
            ParseEnglishSide(englishSide, data);

            // Extract from Chinese side (definition, domain, etc.)
            ParseChineseSide(chineseSide, data);

            return data;
        }

        private static void ParseEnglishSide(string englishText, EnglishChineseParsedData data)
        {
            if (string.IsNullOrWhiteSpace(englishText))
                return;

            // Extract headword (usually first word before space)
            var headwordMatch = Regex.Match(englishText, @"^([A-Za-z0-9\-\.\/]+)");
            if (headwordMatch.Success)
            {
                data.Headword = headwordMatch.Groups[1].Value.Trim();
            }

            // Extract IPA pronunciation (between slashes)
            var ipaMatch = Regex.Match(englishText, @"/([^/]+)/");
            if (ipaMatch.Success)
            {
                data.IpaPronunciation = ipaMatch.Groups[1].Value.Trim();
            }

            // Extract syllabification (words with dots: e.g., "ad·ven·ture")
            var syllabMatch = Regex.Match(englishText, @"([a-zA-Z]+(?:\·[a-zA-Z]+)+)");
            if (syllabMatch.Success)
            {
                data.Syllabification = syllabMatch.Groups[1].Value.Trim();
            }
        }

        private static void ParseChineseSide(string chineseText, EnglishChineseParsedData data)
        {
            if (string.IsNullOrWhiteSpace(chineseText))
                return;

            // Extract POS (part of speech)
            data.PartOfSpeech = ExtractPartOfSpeech(chineseText);

            // Extract domain labels (〔医〕, 〔农〕, etc.)
            data.DomainLabels = ExtractDomainLabels(chineseText);

            // Extract register labels (〈口〉, 〈美〉, etc.)
            data.RegisterLabels = ExtractRegisterLabels(chineseText);

            // Extract etymology
            data.Etymology = ExtractEtymology(chineseText);

            // Extract main definition
            data.MainDefinition = ExtractMainDefinition(chineseText);

            // Extract examples
            data.Examples = ExtractExamples(chineseText);

            // Extract additional senses (1., 2., etc.)
            data.AdditionalSenses = ExtractAdditionalSenses(chineseText);
        }

        public static string? ExtractPartOfSpeech(string chineseText)
        {
            if (string.IsNullOrWhiteSpace(chineseText))
                return null;

            // ENG_CHN POS patterns (abbreviations):
            var posPatterns = new[]
            {
                @"\b(n\.|noun)\b",
                @"\b(v\.|v|verb)\b",
                @"\b(vt\.|vt|transitive verb)\b",
                @"\b(vi\.|vi|intransitive verb)\b",
                @"\b(adj\.|a\.|adjective)\b",
                @"\b(adv\.|ad\.|adverb)\b",
                @"\b(prep\.|preposition)\b",
                @"\b(conj\.|conjunction)\b",
                @"\b(pron\.|pronoun)\b",
                @"\b(int\.|interj\.|interjection)\b",
                @"\b(abbr\.|abbreviation)\b",
                @"\b(phr\.|phrase)\b",
                @"\b(pl\.|plural)\b",
                @"\b(sing\.|singular)\b",
                @"\b(comb\.form|combining form)\b",
                @"\b(suffix)\b",
                @"\b(prefix)\b",
                @"\b(num\.|numeral)\b",
                @"\b(det\.|determiner)\b",
                @"\b(exclam\.|exclamation)\b"
            };

            foreach (var pattern in posPatterns)
            {
                var match = Regex.Match(chineseText, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var pos = match.Value.Trim().ToLowerInvariant();
                    return NormalizeEngChnPartOfSpeech(pos);
                }
            }

            return null;
        }

        public static IReadOnlyList<string> ExtractDomainLabels(string chineseText)
        {
            var domains = new List<string>();

            if (string.IsNullOrWhiteSpace(chineseText))
                return domains;

            // Extract domain labels like 〔医〕, 〔农〕, 〔化〕
            var domainMatches = Regex.Matches(chineseText, @"〔([^〕]+)〕");
            foreach (Match match in domainMatches)
            {
                var domain = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    domains.Add(domain);
                }
            }

            return domains;
        }

        public static IReadOnlyList<string> ExtractRegisterLabels(string chineseText)
        {
            var registers = new List<string>();

            if (string.IsNullOrWhiteSpace(chineseText))
                return registers;

            // Extract register labels like 〈口〉, 〈美〉, 〈英〉, 〈正式〉
            var registerMatches = Regex.Matches(chineseText, @"〈([^〉]+)〉");
            foreach (Match match in registerMatches)
            {
                var register = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(register))
                {
                    registers.Add(register);
                }
            }

            return registers;
        }

        public static string? ExtractEtymology(string chineseText)
        {
            if (string.IsNullOrWhiteSpace(chineseText))
                return null;

            // Extract etymology marked with brackets or "字面意义"
            var etymologyPatterns = new[]
            {
                @"\[\s*(?:<|字面意义：)([^\]]+)\]",  // [<Latin] or [字面意义：from Latin]
                @"来源于\s*([^。]+)",  // 来源于拉丁语
                @"源自\s*([^。]+)",    // 源自希腊语
                @"来自\s*([^。]+)",    // 来自法语
            };

            foreach (var pattern in etymologyPatterns)
            {
                var match = Regex.Match(chineseText, pattern);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
            }

            return null;
        }

        public static string ExtractMainDefinition(string chineseText)
        {
            if (string.IsNullOrWhiteSpace(chineseText))
                return string.Empty;

            // Clean the text: remove domain labels, register labels, etymology
            var cleaned = chineseText;

            // Remove domain labels
            cleaned = Regex.Replace(cleaned, @"〔[^〕]+〕", "");

            // Remove register labels
            cleaned = Regex.Replace(cleaned, @"〈[^〉]+〉", "");

            // Remove etymology
            cleaned = Regex.Replace(cleaned, @"\[\s*(?:<|字面意义：)[^\]]+\]", "");

            // Remove POS markers
            cleaned = Regex.Replace(cleaned, @"\b(?:n\.|v\.|vt\.|vi\.|adj\.|adv\.|prep\.|conj\.|pron\.|int\.|abbr\.|phr\.|pl\.|sing\.|comb\.form|suffix|prefix|num\.|det\.|exclam\.)\b", "", RegexOptions.IgnoreCase);

            // Extract first sense (before any numbered senses)
            var firstSenseMatch = Regex.Match(cleaned, @"^([^1-9]+?)(?=\d+\.|$)");
            if (firstSenseMatch.Success)
            {
                return CleanDefinitionText(firstSenseMatch.Groups[1].Value);
            }

            return CleanDefinitionText(cleaned);
        }

        public static IReadOnlyList<string> ExtractExamples(string chineseText)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(chineseText))
                return examples;

            // ENG_CHN examples are often marked with "例如" or in separate lines
            // Also look for sentence patterns

            // Pattern 1: Lines starting with English example followed by Chinese
            var exampleLines = chineseText.Split('\n');
            foreach (var line in exampleLines)
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("例如") || trimmed.Contains("例句") ||
                    (trimmed.Length > 10 && char.IsUpper(trimmed[0]) && trimmed.Contains('.')))
                {
                    examples.Add(CleanExampleText(trimmed));
                }
            }

            return examples;
        }

        public static IReadOnlyList<EnglishChineseParsedData> ExtractAdditionalSenses(string chineseText)
        {
            var additionalSenses = new List<EnglishChineseParsedData>();

            if (string.IsNullOrWhiteSpace(chineseText))
                return additionalSenses;

            // Extract numbered senses: 2., 3., etc.
            var senseMatches = Regex.Matches(chineseText, @"(\d+)\.\s*(.+?)(?=(?:\d+\.|$))", RegexOptions.Singleline);

            for (int i = 1; i < senseMatches.Count; i++) // Start from 1 to skip first sense
            {
                var match = senseMatches[i];
                var senseDefinition = match.Groups[2].Value.Trim();

                if (!string.IsNullOrWhiteSpace(senseDefinition))
                {
                    var senseData = new EnglishChineseParsedData
                    {
                        MainDefinition = CleanDefinitionText(senseDefinition),
                        SenseNumber = i + 1 // 2, 3, 4...
                    };

                    additionalSenses.Add(senseData);
                }
            }

            return additionalSenses;
        }

        public static string? ExtractDomain(string chineseText)
        {
            var domainLabels = ExtractDomainLabels(chineseText);
            if (domainLabels.Count > 0)
            {
                // Return first domain label, or combine them
                var domain = string.Join(", ", domainLabels);
                return domain.Length <= 100 ? domain : domain.Substring(0, 100);
            }

            var registerLabels = ExtractRegisterLabels(chineseText);
            if (registerLabels.Count > 0)
            {
                // Map Chinese register labels to English
                var mappedRegisters = registerLabels.Select(MapChineseRegisterToEnglish);
                var domain = string.Join(", ", mappedRegisters.Where(x => !string.IsNullOrWhiteSpace(x)));
                return domain.Length <= 100 ? domain : domain.Substring(0, 100);
            }

            return null;
        }

        public static string? ExtractUsageLabel(string chineseText)
        {
            var partOfSpeech = ExtractPartOfSpeech(chineseText);
            var registerLabels = ExtractRegisterLabels(chineseText);

            var usageParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(partOfSpeech))
                usageParts.Add(partOfSpeech);

            if (registerLabels.Count > 0)
                usageParts.AddRange(registerLabels);

            return usageParts.Count > 0 ? string.Join(", ", usageParts) : null;
        }

        // HELPER METHODS
        private static string NormalizeEngChnPartOfSpeech(string pos)
        {
            return pos.ToLowerInvariant().TrimEnd('.') switch
            {
                "n" or "noun" => "noun",
                "v" or "verb" => "verb",
                "vt" or "transitive verb" => "verb_transitive",
                "vi" or "intransitive verb" => "verb_intransitive",
                "a" or "adj" or "adjective" => "adjective",
                "ad" or "adv" or "adverb" => "adverb",
                "prep" or "preposition" => "preposition",
                "conj" or "conjunction" => "conjunction",
                "pron" or "pronoun" => "pronoun",
                "int" or "interj" or "interjection" => "interjection",
                "abbr" or "abbreviation" => "abbreviation",
                "phr" or "phrase" => "phrase",
                "pl" or "plural" => "plural",
                "sing" or "singular" => "singular",
                "comb" or "comb.form" or "combining form" => "combining_form",
                "suffix" => "suffix",
                "prefix" => "prefix",
                "num" or "numeral" => "numeral",
                "det" or "determiner" => "determiner",
                "exclam" or "exclamation" => "exclamation",
                _ => pos
            };
        }

        private static string? MapChineseRegisterToEnglish(string chineseRegister)
        {
            return chineseRegister.ToLowerInvariant() switch
            {
                "口" or "口语" => "colloquial",
                "美" or "美国" => "US",
                "英" or "英国" => "UK",
                "正式" => "formal",
                "非正式" => "informal",
                "书面" => "literary",
                "俚语" => "slang",
                "古" or "古语" => "archaic",
                "旧" or "旧式" => "dated",
                "罕" or "罕见" => "rare",
                "术语" or "专业" => "technical",
                "诗" or "诗歌" => "poetic",
                "幽默" => "humorous",
                "贬" or "贬义" => "derogatory",
                "敬" or "敬语" => "honorific",
                _ => chineseRegister
            };
        }

        private static string CleanDefinitionText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var cleaned = text;

            // Remove excessive whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            // Remove trailing Chinese punctuation
            cleaned = cleaned.TrimEnd('。', '，', '；', '：', '？', '！', '.', ',', ';', ':');

            return cleaned;
        }

        private static string CleanExampleText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var cleaned = text;

            // Remove "例如" markers
            cleaned = cleaned.Replace("例如", "").Replace("例句", "").Trim();

            // Ensure proper English punctuation
            if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
            {
                cleaned += ".";
            }

            return cleaned.Trim();
        }
    }

    // Data class to hold all parsed English-Chinese information
    public class EnglishChineseParsedData
    {
        public string Headword { get; set; } = string.Empty;
        public string? Syllabification { get; set; }
        public string? IpaPronunciation { get; set; }
        public string? PartOfSpeech { get; set; }
        public string MainDefinition { get; set; } = string.Empty;
        public IReadOnlyList<string> DomainLabels { get; set; } = new List<string>();
        public IReadOnlyList<string> RegisterLabels { get; set; } = new List<string>();
        public string? Etymology { get; set; }
        public IReadOnlyList<string> Examples { get; set; } = new List<string>();
        public IReadOnlyList<EnglishChineseParsedData> AdditionalSenses { get; set; } = new List<EnglishChineseParsedData>();
        public int SenseNumber { get; set; } = 1;
        public string EnglishDefinition { get; internal set; }
    }
}