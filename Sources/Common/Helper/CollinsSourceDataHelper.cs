using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Static helper class for Collins dictionary parsing operations.
    /// Contains regex patterns and helper methods for text extraction and cleaning.
    /// </summary>
    public static class CollinsSourceDataHelper
    {
        #region Compiled Regex Patterns (Optimized for Performance)

        public static readonly Regex CfRegex = new(@"\bCf\.?\s+(?<target>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex SeeAlsoRegex = new(@"\bSee also:\s*(?<targets>[^.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex SeeRegex = new(@"\bSee:\s*(?<target>[^.]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex CrossReferenceRegex = new(@"^(?<word>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)→see:\s*(?<target>[a-z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex SynonymPatternRegex = new(
            @"\b(?:synonymous|synonym|same as|equivalent to|also called)\s+(?:[\w\s]*?\s)?(?<word>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex ParentheticalSynonymRegex = new(@"\b(?<word>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\s*\((?:also|syn|syn\.|synonym)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex OrSynonymRegex = new(@"\b(?<word1>[A-Z][a-z]+)\s+or\s+(?<word2>[A-Z][a-z]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex WordBoundaryRegex = new(@"\b[A-Z][a-z]+\b", RegexOptions.Compiled);
        public static readonly Regex EnglishTextRegex = new(@"^[A-Za-z0-9\-\s]+", RegexOptions.Compiled);
        public static readonly Regex EnglishSentenceRegex = new(@"[A-Z][^\.!?]*[\.!?]", RegexOptions.Compiled);
        private static readonly Regex ChineseCharRegex = new(@"[\u4E00-\u9FFF\u3040-\u309F\u30A0-\u30FF]", RegexOptions.Compiled);
        private static readonly Regex ChineseOnlyRegex = new(@"^[\u4E00-\u9FFF\u3040-\u309F\u30A0-\u30FF\s]+$", RegexOptions.Compiled);
        private const string ExampleSeparator = "【Examples】";
        private const string NoteSeparator = "【Note】";
        private const string DomainSeparator = "【Domain】";
        private const string GrammarSeparator = "【Grammar】";
        public static readonly Regex ExampleBulletRegex = new(@"^•\s*(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
        public static readonly Regex DomainLabelRegex = new(@"【([^】]+)】：\s*(.+)", RegexOptions.Compiled);
        public static readonly Regex LabelRegex = new(@"【([^】]+)】[:：]?\s*(.+)", RegexOptions.Compiled);

        public static readonly Regex HeadwordRegex = new(
            @"^★+☆+\s+([A-Za-z][A-Za-z\-\s]+?)\s+●+○+",
            RegexOptions.Compiled);

        public static readonly Regex SenseHeaderEnhancedRegex = new(
            @"^(?:(?<number>\d+)\.\s*)?(?<pos>[A-Z][A-Z\-\s;]+)(?:[/\\]\s*[A-Z][A-Z\-\s;]*)?\t[^\x00-\x7F]*\s*(?<definition>.+)",
            RegexOptions.Compiled);

        public static readonly Regex SenseNumberOnlyRegex = new(@"^(\d+)\.\s*(.+)", RegexOptions.Compiled);
        public static readonly Regex GrammarCodeWithSlashRegex = new(@"^([A-Z][A-Z\-\s;]+)[\t\s]*[/\\]\s*(.+)", RegexOptions.Compiled);
        public static readonly Regex ExampleRegex = new(@"^(?:\.{2,}|…)\s*(?<example>[A-Z].*?[.!?])(?:\s*[^\x00-\x7F]*)?$", RegexOptions.Compiled);
        public static readonly Regex SimpleExampleRegex = new(@"^[A-Z][^.!?]*[.!?](?:\s*[^\x00-\x7F]*)?$", RegexOptions.Compiled);
        public static readonly Regex EllipsisOrDotsRegex = new(@"^(\.{2,}|…)", RegexOptions.Compiled);
        public static readonly Regex PhrasalVerbRegex = new(@"^(?:PHRASAL\s+VERB|PHR\s+V)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex PhrasalPatternRegex = new(@"^(?:PHR(?:-[A-Z]+)+|PHRASAL VERB)\s+(?<definition>[A-Z].+)", RegexOptions.Compiled);
        public static readonly Regex NumberedSectionRegex = new(@"^\d+\.\s+[A-Z][A-Z\s]+USES", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly Regex GrammarCodeRegex = new(
            @"\b(?:V-ERG|N-COUNT|N-UNCOUNT|ADJ-GRADED|PHR-CONJ-SUBORD|PHR-V|PHR-ADV|PHR-PREP|V-TRANS|V-INTRANS|V-REFL|V-PASS)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LeadingStrayPunctuationRegex = new(@"^[\s、。，；,\.\(\)（）:：…\/\\]+", RegexOptions.Compiled);
        private static readonly Regex LeadingJunkRegex = new(@"^[\s、。，；,\.\(\)（）:：…\/\\;]+", RegexOptions.Compiled);

        private static readonly Regex StrayPunctuationPatternsRegex = new(
            @"^(?:DISCOURSE USES \d+|PHR-CONJ-SUBORD|V-ERG\s*[\/\\]|[A-Z]+\s*[\/\\]|\d+\.|[①②③④⑤⑥⑦⑧⑨⑩]|[⓪❶❷❸❹❺❻❼❽❾])\s*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MultiplePunctuationRegex = new(@"[;，。；,\.\(\)（）]{2,}", RegexOptions.Compiled);
        private static readonly Regex TrailingPunctuationRegex = new(@"[\s;，。；,\.\(\)（）]+$", RegexOptions.Compiled);
        private static readonly Regex ExtraSpacesRegex = new(@"\s{2,}", RegexOptions.Compiled);

        private static readonly Regex SectionHeaderRegex = new(
            @"^\s*(?:" +
            @"(?:NOUN|VERB|ADJECTIVE|ADVERB|PRONOUN)\s+AND\s+(?:NOUN|VERB|ADJECTIVE|ADVERB|PRONOUN)\s+USES|" +
            @"\d+\.\s+[A-Z\s]+USES|" +
            @"DISCOURSE\s+USES|" +
            @"ADJECTIVE\s+USES|" +
            @"PHRASAL\s+VERB\s+USES|" +
            @"HIV\s+negative→see:" +
            @")",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly char[] TrimChars = ['.', '。', ' ', '…', '"', '\''];
        private static readonly char[] SeparatorChars = [',', ';', '，', '；'];
        private static readonly string[] SectionMarkers = ["【Examples】", "【Note】", "【Domain】", "【Grammar】"];

        #endregion Compiled Regex Patterns (Optimized for Performance)

        #region Constants and Lookup Tables

        #region Domain and Grammar Code Mappings

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
            ["ADJ CLASSIFIC"] = "adj",
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
            ["ADJ CLASSIF"] = "adj",
            ["ADJ CLASSIFIC"] = "adj",
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
            "N-", "V-", "ADJ-", "ADV-", "PHR-", "PREP", "CONJ", "PRON", "DET", "EXCLAM", "EXCL", "INTERJ", "SUFFIX", "PREFIX", "COMB", "NUM", "AUX", "MODAL", "QUANT"
        };

        #endregion Domain and Grammar Code Mappings

        #endregion Constants and Lookup Tables

        #region CollinsExtractor Helper Methods (Optimized)

        /// <summary>
        /// Checks if the line is an entry separator.
        /// </summary>
        public static bool IsEntrySeparator(string line)
        {
            return !string.IsNullOrEmpty(line) && (line.StartsWith("——————————————", StringComparison.Ordinal) || line.StartsWith("---------------", StringComparison.Ordinal));
        }

        /// <summary>
        /// Tries to parse a headword from the line.
        /// </summary>
        public static bool TryParseHeadword(string line, out string headword)
        {
            headword = string.Empty;
            if (string.IsNullOrEmpty(line)) return false;
            // Accept any line that starts with ★ as a headword (for tests)
            if (line.StartsWith("★"))
            {
                // Remove all star/dot formatting characters
                headword = Regex.Replace(line, @"^[★☆●○▶\.\s]+", "").Trim();
                return !string.IsNullOrEmpty(headword);
            }
            return false;
        }

        public static bool TryParseSenseHeader(string line, out CollinsSenseRaw sense)
        {
            sense = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            // STRICT GUARD:
            // If it looks like continuation text, never start a new sense.
            // (This prevents "2. ..." inside a definition from splitting new senses.)
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

        /// <summary>
        /// Tries to parse a cross-reference from the line.
        /// </summary>
        public static bool TryParseCrossReference(string line, out CrossReference crossRef)
        {
            crossRef = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.Contains("→see:", StringComparison.OrdinalIgnoreCase))
            {
                var match = CrossReferenceRegex.Match(line);
                if (match.Success)
                {
                    crossRef = new CrossReference
                    {
                        TargetWord = match.Groups["target"].Value.Trim(),
                        ReferenceType = "See"
                    };
                    return true;
                }
            }
            if (line.StartsWith("See:", StringComparison.OrdinalIgnoreCase))
            {
                var match = SeeRegex.Match(line);
                if (match.Success)
                {
                    crossRef = new CrossReference
                    {
                        TargetWord = match.Groups["target"].Value.Trim(),
                        ReferenceType = "See"
                    };
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Tries to parse an example from the line.
        /// </summary>
        public static bool TryParseExample(string line, out string example)
        {
            example = string.Empty;
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;
            // Collins examples start with "..."
            if (line.StartsWith("..."))
            {
                example = line.Substring(3).Trim();
                return !string.IsNullOrWhiteSpace(example);
            }
            // Also handle other example indicators
            if (EllipsisOrDotsRegex.IsMatch(line))
            {
                example = line.TrimStart('.', '…', ' ').Trim();
                return !string.IsNullOrWhiteSpace(example);
            }
            return false;
        }

        /// <summary>
        /// Tries to parse a usage note from the line.
        /// </summary>
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

        /// <summary>
        /// Tries to parse a domain/grammar label from the line.
        /// </summary>
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

        /// <summary>
        /// Fast POS normalization with optimized lookup.
        /// </summary>
        public static string NormalizePos(string rawPos)
        {
            return FastNormalizePos(rawPos);
        }

        /// <summary>
        /// Checks if a line could be a definition continuation.
        /// </summary>
        // In CollinsSourceDataHelper.cs - update IsDefinitionContinuation:
        public static bool IsDefinitionContinuation(string line, string currentDefinition)
        {
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(currentDefinition)) return false;
            var trimmed = line.TrimStart();
            // More lenient: allow continuation if line starts with lowercase
            // or common continuation patterns
            if (trimmed.Length > 0 && char.IsLower(trimmed[0])) return true;
            if (trimmed.StartsWith("or ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("but ", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("(") && trimmed.Length > 1) return true;
            // Also continue if previous definition ends with certain punctuation
            if (currentDefinition.EndsWith(",") || currentDefinition.EndsWith(";"))
            {
                if (char.IsLetter(trimmed[0])) return true;
            }
            return false;
        }

        #endregion CollinsExtractor Helper Methods (Optimized)

        #region Text Processing and Cleaning (Optimized)

        /// <summary>
        /// Removes Chinese characters from text.
        /// </summary>
        public static string RemoveChineseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            // FIX: Only remove Chinese characters for English-only processing
            // For Collins bilingual format, we should preserve Chinese
            return ChineseCharRegex.Replace(text, "");
        }

        /// <summary>
        /// Cleans example text with optimized operations.
        /// </summary>
        public static string CleanExampleText(string example)
        {
            return FastCleanExampleText(example);
        }

        /// <summary>
        /// Fast example text cleaning.
        /// </summary>
        private static string FastCleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example)) return example;
            var cleaned = example;
            // FIX: Don't remove Chinese characters from Collins examples
            // They contain bilingual translations
            if (cleaned.Contains("...") || cleaned.Contains("…"))
                cleaned = cleaned.Replace("…", "...").Replace("．．．", "...");
            cleaned = cleaned.Trim(TrimChars);
            cleaned = FastFixTextIssues(cleaned);
            if (string.IsNullOrWhiteSpace(cleaned)) return cleaned;
            if (char.IsLower(cleaned[0])) cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
            if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
                cleaned += ".";
            return cleaned.Trim();
        }

        /// <summary>
        /// Cleans text with specified options.
        /// </summary>
        public static string CleanText(string text, bool removeChinese = false, bool normalizePunctuation = true)
        {
            return FastCleanText(text, removeChinese, normalizePunctuation);
        }

        /// <summary>
        /// Fast text cleaning with minimal allocations.
        /// </summary>
        private static string FastCleanText(string text, bool removeChinese, bool normalizePunctuation)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = text;
            if (removeChinese) result = ChineseCharRegex.Replace(result, "");
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
            if (result.Contains(" ")) result = ExtraSpacesRegex.Replace(result, " ");
            return result.Trim();
        }

        /// <summary>
        /// Fast text issue fixing.
        /// </summary>
        private static string FastFixTextIssues(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = text;
            result = result.Replace("、", ", ").Replace("。", ". ")
                .Replace("；", "; ").Replace("：", ": ")
                .Replace("…", "...").Replace("．．．", "...")
                .Replace(" ,", ",").Replace(" .", ".")
                .Replace(" ;", ";").Replace(" :", ":")
                .Replace("( ", "(").Replace(" )", ")")
                .Replace("?.", "?").Replace("!.", "!")
                .Replace("..", ".");
            while (result.Contains("  ")) result = result.Replace("  ", " ");
            return result.Trim();
        }

        /// <summary>
        /// Extracts clean domain code with optimized lookups.
        /// </summary>
        public static string? ExtractCleanDomain(string? domainText)
        {
            if (string.IsNullOrWhiteSpace(domainText)) return null;
            var text = domainText.Trim();
            foreach (var kvp in ChineseDomainMap)
                if (text.Contains(kvp.Key)) return kvp.Value;
            foreach (var kvp in DomainCodeMap)
                if (text.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0) return kvp.Value;
            return null;
        }

        /// <summary>
        /// Extracts clean grammar code.
        /// </summary>
        public static string? ExtractCleanGrammar(string? grammarText)
        {
            if (string.IsNullOrWhiteSpace(grammarText)) return null;
            var cleaned = FastCleanText(grammarText.Trim(), true, true);
            if (string.IsNullOrWhiteSpace(cleaned)) return null;
            if (cleaned.StartsWith("PHRASAL VERB", StringComparison.OrdinalIgnoreCase) ||
                cleaned.StartsWith("PHR V", StringComparison.OrdinalIgnoreCase))
                return "phrasal_verb";
            for (var i = 0; i < cleaned.Length; i++)
                if (!char.IsLetterOrDigit(cleaned[i]) && cleaned[i] != '-' && cleaned[i] != ' ')
                {
                    var result = cleaned.Substring(0, i).Trim();
                    return result.Length <= 100 ? result : null;
                }
            return cleaned.Length <= 100 ? cleaned : null;
        }

        #endregion Text Processing and Cleaning (Optimized)

        #region Section Extraction (Optimized with String Operations)

        /// <summary>
        /// Extracts the main definition text (before any section markers).
        /// </summary>
        public static string ExtractMainDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return string.Empty;
            var firstIndex = definition.Length;
            foreach (var marker in SectionMarkers)
            {
                var index = definition.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0 && index < firstIndex) firstIndex = index;
            }
            var result = definition.Substring(0, firstIndex).Trim();
            return CleanDefinitionText(result);
        }

        /// <summary>
        /// Extracts example sentences from definition text.
        /// </summary>
        public static IReadOnlyList<string> ExtractExamples(string definition)
        {
            var examples = new List<string>();
            var exampleIndex = definition.IndexOf(ExampleSeparator, StringComparison.Ordinal);
            if (exampleIndex >= 0)
            {
                var start = exampleIndex + ExampleSeparator.Length;
                var end = FastFindNextMarker(definition, start);
                if (end > start)
                {
                    var exampleSection = definition.Substring(start, end - start);
                    var lines = exampleSection.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("•"))
                        {
                            var example = trimmed.Substring(1).Trim();
                            example = FastCleanExampleText(example);
                            if (!string.IsNullOrWhiteSpace(example)) examples.Add(example);
                        }
                    }
                }
            }
            return examples;
        }

        /// <summary>
        /// Extracts notes from definition text.
        /// </summary>
        public static string? ExtractNotes(string definition)
        {
            var noteIndex = definition.IndexOf(NoteSeparator, StringComparison.Ordinal);
            if (noteIndex >= 0)
            {
                var start = noteIndex + NoteSeparator.Length;
                var end = FastFindNextMarker(definition, start);
                return definition.Substring(start, end - start).Trim();
            }
            return null;
        }

        /// <summary>
        /// Extracts domain information from definition text.
        /// </summary>
        public static string? ExtractDomain(string definition)
        {
            var domainIndex = definition.IndexOf(DomainSeparator, StringComparison.Ordinal);
            if (domainIndex >= 0)
            {
                var start = domainIndex + DomainSeparator.Length;
                var end = FastFindNextMarker(definition, start);
                var domainText = definition.Substring(start, end - start).Trim();
                return ExtractCleanDomain(domainText);
            }
            return null;
        }

        /// <summary>
        /// Extracts grammar information from definition text.
        /// </summary>
        public static string? ExtractGrammar(string definition)
        {
            var grammarIndex = definition.IndexOf(GrammarSeparator, StringComparison.Ordinal);
            if (grammarIndex >= 0)
            {
                var start = grammarIndex + GrammarSeparator.Length;
                var end = FastFindNextMarker(definition, start);
                var grammarText = definition.Substring(start, end - start).Trim();
                return ExtractCleanGrammar(grammarText);
            }
            return null;
        }

        /// <summary>
        /// Fast marker finding using string operations.
        /// </summary>
        public static int FindNextMarker(string text, int startIndex)
        {
            return FastFindNextMarker(text, startIndex);
        }

        private static int FastFindNextMarker(string text, int startIndex)
        {
            var nextIndex = text.Length;
            foreach (var marker in SectionMarkers)
            {
                var index = text.IndexOf(marker, startIndex, StringComparison.Ordinal);
                if (index >= 0 && index < nextIndex) nextIndex = index;
            }
            return nextIndex;
        }

        #endregion Section Extraction (Optimized with String Operations)

        #region Cross-Reference Extraction (Optimized)

        public static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
        {
            var crossRefs = new List<CrossReference>();
            if (string.IsNullOrWhiteSpace(definition)) return crossRefs;
            crossRefs.AddRange(FastExtractCrossReferencesFromText(definition));
            var note = ExtractNotes(definition);
            if (!string.IsNullOrEmpty(note)) crossRefs.AddRange(FastExtractCrossReferencesFromText(note));
            var seen = new HashSet<(string, string)>();
            var uniqueRefs = new List<CrossReference>();
            foreach (var cr in crossRefs)
            {
                if (cr == null) continue;
                var target = (cr.TargetWord ?? string.Empty).Trim();
                var type = (cr.ReferenceType ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(type)) continue;
                // hard safety: avoid huge targets being stored
                if (target.Length > 150) continue;
                var key = (target.ToLowerInvariant(), type);
                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    uniqueRefs.Add(new CrossReference { TargetWord = target, ReferenceType = type });
                }
            }
            return uniqueRefs;
        }

        private static IReadOnlyList<CrossReference> FastExtractCrossReferencesFromText(string text)
        {
            var crossRefs = new List<CrossReference>();
            if (string.IsNullOrWhiteSpace(text)) return crossRefs;
            // 1) Handle "→see:" style references safely (multiple in one blob)
            if (text.Contains("→see:", StringComparison.OrdinalIgnoreCase))
            {
                // pattern: "... →see: cover; ... →see: hot; ... →see: kiss"
                var matches = Regex.Matches(
                    text,
                    @"→see:\s*(?<target>[A-Za-z][A-Za-z\s\-']*)",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
                foreach (Match m in matches)
                {
                    var target = m.Groups["target"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        crossRefs.Add(new CrossReference
                        {
                            TargetWord = target,
                            ReferenceType = "See"
                        });
                    }
                }
            }
            // 2) Handle "See also:" safely (split by separators)
            if (text.Contains("See also:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in SeeAlsoRegex.Matches(text))
                {
                    var targetsText = match.Groups["targets"].Value.Trim();
                    var targets = FastParseTargetWords(targetsText);
                    foreach (var target in targets)
                    {
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            crossRefs.Add(new CrossReference
                            {
                                TargetWord = target,
                                ReferenceType = "SeeAlso"
                            });
                        }
                    }
                }
            }
            // 3) Handle "See:" safely (split instead of taking full [^.]+ blob)
            if (text.Contains("See:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in SeeRegex.Matches(text))
                {
                    var raw = match.Groups["target"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    // IMPORTANT FIX:
                    // old regex captured huge text until '.' and caused truncation
                    // now split it into clean targets
                    var targets = FastParseTargetWords(raw);
                    foreach (var target in targets)
                    {
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            crossRefs.Add(new CrossReference
                            {
                                TargetWord = target,
                                ReferenceType = "See"
                            });
                        }
                    }
                }
            }
            // 4) Handle "Cf." safely (keep existing single-target logic)
            if (text.Contains("Cf.", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match match in CfRegex.Matches(text))
                {
                    var target = match.Groups["target"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        crossRefs.Add(new CrossReference
                        {
                            TargetWord = target,
                            ReferenceType = "Cf"
                        });
                    }
                }
            }
            return crossRefs;
        }

        private static IReadOnlyList<string> FastParseTargetWords(string targetsText)
        {
            var targets = new List<string>();
            if (string.IsNullOrWhiteSpace(targetsText)) return targets;
            // split by known separators
            var parts = targetsText.Split(SeparatorChars, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                // drop trailing connectors
                if (trimmed.EndsWith(" and", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(0, trimmed.Length - 4);
                else if (trimmed.EndsWith(" or", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(0, trimmed.Length - 3);
                // remove chinese + extra symbols
                trimmed = ChineseCharRegex.Replace(trimmed, "")
                    .Trim(',', ';', ' ', '，', '；', '.', '。');
                // also cut at arrow see fragments if someone pasted a whole blob
                var arrowIndex = trimmed.IndexOf("→see:", StringComparison.OrdinalIgnoreCase);
                if (arrowIndex >= 0) trimmed = trimmed.Substring(0, arrowIndex).Trim();
                // basic cleanup: remove obvious non-word junk
                trimmed = Regex.Replace(trimmed, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) targets.Add(trimmed);
            }
            return targets;
        }

        #endregion Cross-Reference Extraction (Optimized)

        #region Synonym Extraction (Optimized)

        /// <summary>
        /// Extracts synonyms from example sentences.
        /// </summary>
        public static IReadOnlyList<string>? ExtractSynonymsFromExamples(IReadOnlyList<string> examples)
        {
            if (examples == null || examples.Count == 0) return null;
            var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var example in examples)
                FastAddSynonymsFromText(example, synonyms);
            return synonyms.Count > 0 ? new List<string>(synonyms) : null;
        }

        /// <summary>
        /// Fast synonym extraction.
        /// </summary>
        private static void FastAddSynonymsFromText(string text, HashSet<string> synonyms)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var synonymMatch = SynonymPatternRegex.Match(text);
            if (synonymMatch.Success) FastAddSynonym(synonymMatch.Groups["word"].Value, synonyms);
            var parentheticalMatch = ParentheticalSynonymRegex.Match(text);
            if (parentheticalMatch.Success) FastAddSynonym(parentheticalMatch.Groups["word"].Value, synonyms);
            var orMatch = OrSynonymRegex.Match(text);
            if (orMatch.Success)
            {
                FastAddSynonym(orMatch.Groups["word1"].Value, synonyms);
                FastAddSynonym(orMatch.Groups["word2"].Value, synonyms);
            }
        }

        private static void FastAddSynonym(string word, HashSet<string> synonyms)
        {
            var trimmed = word.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed)) synonyms.Add(trimmed.ToLowerInvariant());
        }

        #endregion Synonym Extraction (Optimized)

        #region Raw Fragment Processing (Optimized)

        /// <summary>
        /// Extracts domain code from the raw fragment text.
        /// </summary>
        public static string? ExtractDomainFromRawFragment(string rawFragment)
        {
            if (string.IsNullOrWhiteSpace(rawFragment)) return null;
            var labelIndex = rawFragment.IndexOf("【语域标签】：", StringComparison.Ordinal);
            if (labelIndex >= 0)
            {
                var start = labelIndex + "【语域标签】：".Length;
                var end = rawFragment.IndexOf("】", start);
                if (end >= 0)
                {
                    var domainText = rawFragment.Substring(start, end - start).Trim();
                    return ExtractCleanDomain(domainText);
                }
            }
            if (rawFragment.Contains("正式")) return "FORMAL";
            if (rawFragment.Contains("非正式")) return "INFORMAL";
            if (rawFragment.Contains("主美") || rawFragment.Contains("美式")) return "US";
            if (rawFragment.Contains("主英") || rawFragment.Contains("英式")) return "UK";
            return null;
        }

        /// <summary>
        /// Extracts the main definition from raw fragment.
        /// </summary>
        public static string ExtractMainDefinitionFromRawFragment(string rawFragment)
        {
            if (string.IsNullOrWhiteSpace(rawFragment)) return string.Empty;
            var firstIndex = rawFragment.Length;
            foreach (var marker in SectionMarkers)
            {
                var index = rawFragment.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0 && index < firstIndex) firstIndex = index;
            }
            var mainDefinition = rawFragment.Substring(0, firstIndex).Trim();
            return FastCleanDefinitionText(mainDefinition);
        }

        /// <summary>
        /// Processes and cleans a batch of definitions from database output.
        /// </summary>
        public static List<string> CleanDefinitionBatch(IEnumerable<string> definitions)
        {
            var results = new List<string>();
            foreach (var definition in definitions)
            {
                if (string.IsNullOrWhiteSpace(definition))
                {
                    results.Add(string.Empty);
                    continue;
                }
                var withoutSections = definition;
                foreach (var marker in SectionMarkers)
                {
                    var index = withoutSections.IndexOf(marker, StringComparison.Ordinal);
                    if (index >= 0) withoutSections = withoutSections.Substring(0, index);
                }
                results.Add(FastCleanDefinitionText(withoutSections));
            }
            return results;
        }

        /// <summary>
        /// Fast raw definition fragment fixing.
        /// </summary>
        public static string FixRawDefinitionFragment(string rawFragment)
        {
            if (string.IsNullOrWhiteSpace(rawFragment)) return string.Empty;
            var result = rawFragment;
            if (result.StartsWith("、") || result.StartsWith("…")) result = result.Substring(1).TrimStart();
            if (result.StartsWith("DISCOURSE USES", StringComparison.OrdinalIgnoreCase))
                result = result.Substring("DISCOURSE USES".Length).TrimStart();
            var match = EnglishSentenceRegex.Match(result);
            if (match.Success) result = match.Value;
            return FastCleanDefinitionText(result);
        }

        #endregion Raw Fragment Processing (Optimized)

        #region Universal Text Cleaning Methods (Optimized)

        /// <summary>
        /// Universal definition cleaner - optimized version.
        /// </summary>
        public static string CleanDefinitionText(string definition)
        {
            var cleaned = FastCleanDefinitionText(definition);
            if (string.IsNullOrWhiteSpace(cleaned) && !string.IsNullOrWhiteSpace(definition))
                cleaned = LenientCleanDefinitionText(definition);
            return cleaned;
        }

        /// <summary>
        /// More lenient cleaning for definitions that might get filtered out.
        /// </summary>
        private static string LenientCleanDefinitionText(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return definition;
            var result = definition;
            result = ChineseCharRegex.Replace(result, "");
            result = Regex.Replace(result, @"^[\s、。，；,\.\(\)（）:：…\/\\]+", "");
            result = FastFixTextIssues(result);
            result = FastEnsureSentenceStructure(result);
            if (string.IsNullOrWhiteSpace(result)) result = ChineseCharRegex.Replace(definition, "").Trim();
            return result.Trim();
        }

        /// <summary>
        /// Fast definition cleaning with minimal allocations.
        /// </summary>
        private static string FastCleanDefinitionText(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return definition;
            if (IsNonDefinition(definition)) return string.Empty;
            var cleaned = FastExtractDefinitionCore(definition);
            if (string.IsNullOrWhiteSpace(cleaned)) return cleaned;
            cleaned = FastRemoveStrayElements(cleaned);
            cleaned = FastCleanText(cleaned, true, true);
            cleaned = FastFixTextFormatting(cleaned);
            cleaned = FastEnsureSentenceStructure(cleaned);
            if (!string.IsNullOrEmpty(cleaned)) cleaned = FastEnsureCapitalizationAndPunctuation(cleaned);
            return cleaned.Trim();
        }

        /// <summary>
        /// Advanced definition cleaner with pattern detection.
        /// </summary>
        public static string AdvancedCleanDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return definition;
            if (IsNonDefinition(definition)) return string.Empty;
            return FastCleanDefinitionText(definition);
        }

        /// <summary>
        /// Fast batch processing.
        /// </summary>
        public static List<string> ProcessDefinitionBatch(IEnumerable<string> definitions)
        {
            var results = new List<string>();
            foreach (var definition in definitions)
                try
                {
                    if (string.IsNullOrWhiteSpace(definition))
                    {
                        results.Add(string.Empty);
                        continue;
                    }
                    if (IsNonDefinition(definition))
                    {
                        results.Add(string.Empty);
                        continue;
                    }
                    var cleaned = AdvancedCleanDefinition(definition);
                    results.Add(FastIsValidDefinition(cleaned, true) ? cleaned : string.Empty);
                }
                catch
                {
                    results.Add(FastCleanDefinitionText(definition));
                }
            return results;
        }

        #endregion Universal Text Cleaning Methods (Optimized)

        #region Private Helper Methods (Optimized for Speed)

        private static CollinsSenseRaw? CreateSenseFromMatch(Match match)
        {
            if (match == null || !match.Success) return null;
            var numberGroup = match.Groups["number"];
            var posGroup = match.Groups["pos"];
            var definitionGroup = match.Groups["definition"];
            if (!posGroup.Success || !definitionGroup.Success) return null;
            var senseNumber = numberGroup.Success && int.TryParse(numberGroup.Value, out var parsed) ? parsed : 1;
            var pos = posGroup.Value.Trim();
            var definition = definitionGroup.Value.Trim();
            if (string.IsNullOrWhiteSpace(pos) || string.IsNullOrWhiteSpace(definition)) return null;
            definition = ChineseCharRegex.Replace(definition, "").Trim();
            var cleanedDefinition = FastCleanDefinitionText(definition);
            if (string.IsNullOrWhiteSpace(cleanedDefinition)) cleanedDefinition = LenientCleanDefinitionText(definition);
            return new CollinsSenseRaw
            {
                SenseNumber = senseNumber,
                PartOfSpeech = FastNormalizePos(pos),
                Definition = cleanedDefinition
            };
        }

        private static int FastExtractSenseNumber(string posText)
        {
            if (string.IsNullOrWhiteSpace(posText)) return 1;
            if (posText.Length > 0 && char.IsDigit(posText[0]))
            {
                var dotIndex = posText.IndexOf('.');
                if (dotIndex > 0 && int.TryParse(posText.AsSpan(0, dotIndex), out var number))
                    return number;
            }
            return 1;
        }

        private static string FastNormalizePos(string rawPos)
        {
            if (string.IsNullOrWhiteSpace(rawPos)) return "unk";
            var trimmed = rawPos.Trim();
            trimmed = Regex.Replace(trimmed, @"^\d+\.\s*", "");
            if (PosNormalizationMap.TryGetValue(trimmed, out var exactMatch)) return exactMatch;
            var noChinese = ChineseCharRegex.Replace(trimmed, "").Trim();
            if (PosNormalizationMap.TryGetValue(noChinese, out var noChineseMatch)) return noChineseMatch;
            foreach (var prefix in PosPrefixes)
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var slashIndex = trimmed.IndexOf('/');
                    if (slashIndex > 0)
                    {
                        var firstPart = trimmed.Substring(0, slashIndex).Trim();
                        if (PosNormalizationMap.TryGetValue(firstPart, out var slashMatch))
                            return slashMatch;
                    }
                    if (PosNormalizationMap.TryGetValue(prefix, out var prefixMatch)) return prefixMatch;
                    if (prefix.StartsWith("N-", StringComparison.OrdinalIgnoreCase) || prefix == "N") return "noun";
                    if (prefix.StartsWith("V-", StringComparison.OrdinalIgnoreCase) || prefix == "V") return "verb";
                    if (prefix.StartsWith("ADJ-", StringComparison.OrdinalIgnoreCase) || prefix == "ADJ") return "adj";
                    if (prefix.StartsWith("ADV-", StringComparison.OrdinalIgnoreCase) || prefix == "ADV") return "adv";
                    if (prefix.StartsWith("PHR-", StringComparison.OrdinalIgnoreCase)) return "phrase";
                }
            if (trimmed.Contains("NOUN", StringComparison.OrdinalIgnoreCase)) return "noun";
            if (trimmed.Contains("VERB", StringComparison.OrdinalIgnoreCase)) return "verb";
            if (trimmed.Contains("ADJECTIVE", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("ADJ", StringComparison.OrdinalIgnoreCase))
                return "adj";
            if (trimmed.Contains("ADVERB", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("ADV", StringComparison.OrdinalIgnoreCase))
                return "adv";
            return "unk";
        }

        private static CollinsSenseRaw? TryParseEnhancedSenseHeader(string line)
        {
            var match = SenseHeaderEnhancedRegex.Match(line);
            return match.Success ? CreateSenseFromMatch(match) : null;
        }

        private static CollinsSenseRaw? TryParseGrammarCodeWithSlash(string line)
        {
            var match = GrammarCodeWithSlashRegex.Match(line);
            if (match.Success)
                return new CollinsSenseRaw
                {
                    SenseNumber = ExtractSenseNumberFromText(line),
                    PartOfSpeech = FastNormalizePos(match.Groups[1].Value.Trim()),
                    Definition = CleanDefinitionText(match.Groups[2].Value.Trim())
                };
            return null;
        }

        private static CollinsSenseRaw? TryParsePhrasalPattern(string line)
        {
            var match = PhrasalPatternRegex.Match(line);
            if (match.Success)
                return new CollinsSenseRaw
                {
                    SenseNumber = ExtractSenseNumberFromText(line),
                    PartOfSpeech = FastNormalizePos(match.Groups[1].Value.Trim()),
                    Definition = CleanDefinitionText(match.Groups["definition"].Value.Trim())
                };
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
            // STRICT:
            // Do not treat "2. ..." as a new sense header if it's likely just a continuation.
            // Collins real sense headers almost always include POS info or special structure.
            if (string.IsNullOrWhiteSpace(line)) return null;
            // quick reject: if line has Chinese markers or section markers, it's not a new sense header
            if (line.Contains("【") || line.Contains("】")) return null;
            // must match "2. something"
            var match = SenseNumberOnlyRegex.Match(line);
            if (!match.Success) return null;
            var rest = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(rest)) return null;
            // ✅ CRITICAL FIX:
            // If the remaining content starts with lowercase or punctuation, it's likely continuation
            if (rest.Length > 0 && !char.IsUpper(rest[0])) return null;
            // ✅ CRITICAL FIX:
            // If it contains "→see:" it is NOT a sense, it's cross-reference line
            if (rest.Contains("→see:", StringComparison.OrdinalIgnoreCase)) return null;
            // ✅ CRITICAL FIX:
            // If it's clearly a continuation connector, skip it
            if (rest.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("or ", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("but ", StringComparison.OrdinalIgnoreCase)) return null;
            return new CollinsSenseRaw
            {
                SenseNumber = int.TryParse(match.Groups[1].Value, out var num) ? num : 1,
                PartOfSpeech = "unk",
                Definition = CleanDefinitionText(rest)
            };
        }

        private static int ExtractSenseNumberFromText(string text)
        {
            var match = Regex.Match(text, @"^(\d+)\.\s*");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number)) return number;
            return 1;
        }

        private static string FastExtractDefinitionCore(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = text;
            result = FastRemoveSectionHeaders(result);
            result = FastRemoveGrammarCodes(result);
            result = FastRemoveCrossReferences(result);
            return FastExtractActualDefinition(result);
        }

        private static string FastRemoveSectionHeaders(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (text.StartsWith("NOUN AND VERB USES", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("DISCOURSE USES", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("PHRASAL VERB USES", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return text;
        }

        private static string FastRemoveGrammarCodes(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = text.TrimStart();
            if (result.StartsWith("N-") || result.StartsWith("V-") || result.StartsWith("ADJ-") ||
                result.StartsWith("ADV-") || result.StartsWith("PHR-"))
            {
                var endIndex = 0;
                while (endIndex < result.Length && (char.IsLetterOrDigit(result[endIndex]) || result[endIndex] == '-'))
                    endIndex++;
                if (endIndex < result.Length) result = result.Substring(endIndex).TrimStart();
                else result = string.Empty;
            }
            return result;
        }

        private static string FastRemoveCrossReferences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (text.StartsWith("See also:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("See:", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Cf.", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return text;
        }

        private static string FastExtractActualDefinition(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            for (var i = 0; i < text.Length; i++)
                if (char.IsUpper(text[i]))
                {
                    for (var j = i; j < text.Length; j++)
                        if (text[j] == '.' || text[j] == '!' || text[j] == '?')
                            return text.Substring(i, j - i + 1).Trim();
                    return text.Substring(i).Trim();
                }
            return text;
        }

        private static string FastRemoveStrayElements(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = text;
            result = result.TrimStart(' ', '、', '。', '，', '；', ',', '.', '(', ')', '（', '）', ':', '：', '…', '/', '\\', ';');
            if (result.Length > 0 && char.IsDigit(result[0]))
            {
                var i = 0;
                while (i < result.Length && char.IsDigit(result[i])) i++;
                if (i < result.Length && result[i] == '.') result = result.Substring(i + 1).TrimStart();
            }
            return result;
        }

        private static string FastFixTextFormatting(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = text;
            result = result.Replace("  ", " ")
                .Replace(" ,", ",").Replace(" .", ".")
                .Replace(" ;", ";").Replace(" :", ":")
                .Replace("( ", "(").Replace(" )", ")")
                .Replace("..", ".").Replace("?.", "?")
                .Replace("!.", "!");
            return result.Trim();
        }

        private static string FastEnsureSentenceStructure(string text)
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
                    else result += ".";
                }
            }
            return result;
        }

        private static string FastEnsureCapitalizationAndPunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var result = text;
            if (char.IsLower(result[0])) result = char.ToUpper(result[0]) + result.Substring(1);
            var lastChar = result[^1];
            if (!(lastChar == '.' || lastChar == '!' || lastChar == '?' || lastChar == ':'))
                result += ".";
            return result;
        }

        private static bool IsNonDefinition(string text)
        {
            return IsSectionHeader(text) || IsCrossReferenceOnly(text) || IsGrammarCodeOnly(text);
        }

        private static bool IsSectionHeader(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && SectionHeaderRegex.IsMatch(text);
        }

        private static bool IsCrossReferenceOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var trimmed = text.Trim();
            return trimmed.StartsWith("See also:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("See:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Cf.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGrammarCodeOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var trimmed = text.Trim();
            if (trimmed.Contains("/") && !trimmed.Contains(" ")) return true;
            if (trimmed.StartsWith("N-") || trimmed.StartsWith("V-") || trimmed.StartsWith("ADJ-"))
                return !trimmed.Contains(" ") || trimmed.IndexOf(' ') > trimmed.Length / 2;
            return false;
        }

        private static bool FastIsValidDefinition(string text, bool lenient = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var trimmed = text.Trim();
            if (lenient && trimmed.Length < 10) return true;
            if (!lenient && text.Length < 10) return false;
            if (!char.IsUpper(trimmed[0])) return false;
            if (!trimmed.Contains(' ') && trimmed.Length > 20) return false;
            if (!trimmed.Any(char.IsLower)) return false;
            if (!trimmed.EndsWith(".") && !trimmed.EndsWith("!") && !trimmed.EndsWith("?")) return false;
            return true;
        }

        private static bool ContainsLowerCase(string text)
        {
            foreach (var c in text)
                if (char.IsLower(c)) return true;
            return false;
        }

        #endregion Private Helper Methods (Optimized for Speed)
    }
}