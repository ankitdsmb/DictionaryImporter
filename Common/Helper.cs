using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Infrastructure.PostProcessing.Enrichment;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using NTextCat;

namespace DictionaryImporter.Common
{
    public static class Helper
    {
        public const int MAX_RECORDS_PER_SOURCE = 100;

        // =====================================================================
        // 1) REGEX (ALL AT TOP)
        // =====================================================================

        private static readonly Regex RxWhitespace =
            new(@"\s+", RegexOptions.Compiled);

        private static readonly Regex RxHasEnglishLetter =
            new("[A-Za-z]", RegexOptions.Compiled);

        private static readonly Regex RxHasCjk =
            new(@"[\u4E00-\u9FFF]", RegexOptions.Compiled);

        private static readonly Regex RxNonWordForNormalizedWord =
            new(@"[^\p{L}\p{N}\s\-']", RegexOptions.Compiled);

        private static readonly Regex RxNoiseLettersOnly =
            new(@"[^\p{L}\s]", RegexOptions.Compiled);

        private static readonly Regex RxIpaSlashBlock =
            new(@"/[^/]+/", RegexOptions.Compiled);

        private static readonly Regex RxEnglishOrthographicSyllableLine =
            new(@"^\s*[A-Za-z]+(?:·[A-Za-z]+)+\s*", RegexOptions.Compiled);

        private static readonly Regex RxLeadingPos =
            new(
                @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s+",
                RegexOptions.IgnoreCase | RegexOptions.Compiled
            );

        private static readonly Regex RxOxfordLeadingDomain =
            new(@"^\(([^)]+)\)", RegexOptions.Compiled);

        private static readonly Regex RxGutenbergDomain =
            new(@"[<\(]([^>)]+)[>\)]", RegexOptions.Compiled);

        private static readonly Regex RxChnDomain =
            new(@"〔([^〕]+)〕", RegexOptions.Compiled);

        private static readonly Regex RxKaikkiDomainStrip =
            new(@"[<>\(\)]", RegexOptions.Compiled);

        private static readonly Regex RxDomainMarkerStrip =
            new(@"^[\(\[【].+?[\)\]】]\s*", RegexOptions.Compiled);

        private static readonly Regex RxCjkPunctuation =
            new(@"[，。、；：！？【】（）《》〈〉「」『』]", RegexOptions.Compiled);

        private static readonly Regex RxCjkBlocks =
            new(@"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF]", RegexOptions.Compiled);

        private static readonly Regex RxWordSanitizer =
            new(@"[^A-Za-z'\-]", RegexOptions.Compiled);

        private static readonly Regex RxOrthographicVowel =
            new(@"[aeiouyAEIOUY]", RegexOptions.Compiled);

        // IPA extraction / cleanup regex
        private static readonly Regex RxIpaSlashCore =
            new(@"/([^/]+)/", RegexOptions.Compiled);

        private static readonly Regex RxIpaPresence =
            new(@"[ˈˌɑ-ʊəɐɛɪɔʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃ː]", RegexOptions.Compiled);

        private static readonly Regex RxIpaAllowedChars =
            new(@"[^ˈˌɑ-ʊəɐɛɪɔʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃ː\. ]", RegexOptions.Compiled);

        private static readonly Regex RxIpaReject =
            new(@"^[0-9\s./:-]+$", RegexOptions.Compiled);

        private static readonly Regex RxIpaProseReject =
            new(@"\b(strong|weak|form|plural|singular)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RxIpaEditorialPunctuation =
            new(@"[.,，]", RegexOptions.Compiled);

        private static readonly Regex RxParen =
            new(@"[\(\)]", RegexOptions.Compiled);

        private static readonly Regex RxEdgeHyphen =
            new(@"(^-)|(-$)", RegexOptions.Compiled);

        private static readonly Regex RxIpaStress =
            new(@"[ˈˌ]", RegexOptions.Compiled);

        private static readonly Regex RxIpaVowelForStressInjection =
            new(@"[ɑæɐəɛɪiɔʊuʌeɜ]", RegexOptions.Compiled);

        private static readonly Regex RxIpaSyllableVowel =
            new(@"[aeiouæɪʊəɐɑɔɛɜʌoøɒyɯɨɶ]", RegexOptions.Compiled);

        private static readonly Regex RxIpaSyllableConsonant =
            new(@"[bcdfghjklmnpqrstvwxyzθðʃʒŋ]", RegexOptions.Compiled);

        private static readonly Regex RxIpaAmericanMarkers =
            new(@"[ɹɑɚɝoʊ]", RegexOptions.Compiled);

        private static readonly Regex RxIpaBritishMarkers =
            new(@"[ɒəʊː]", RegexOptions.Compiled);

        // =====================================================================
        // 2) LOOKUPS / CONSTANT LISTS
        // =====================================================================

        private static readonly HashSet<string> BilingualSources =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "ENG_CHN",
                "CENTURY21",
                "ENG_COLLINS"
            };

        private static readonly string[] DomainDefinitionIndicators =
        {
            "hours", "days", "weeks", "minutes", "seconds", "o'clock"
        };

        private static readonly HashSet<string> OrthographicDigraphConsonants =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "ch", "sh", "th", "ph", "wh",
                "ck", "ng", "gh", "gn", "kn", "wr",
                "qu"
            };

        private static readonly string[] OrthographicStrongSuffixes =
        {
            "ation", "ition", "tation",
            "tion", "sion",
            "ture", "sure",
            "cial", "tial", "cian", "gian",
            "ment", "ness",
            "able", "ible",
            "ing", "edly", "ed",
            "er", "est", "ly",
            "ious", "eous", "uous",
            "ative", "itive",
            "ize", "ise",
            "ate"
        };

        // =====================================================================
        // 4) CORE NORMALIZATION (Shared building blocks)
        // =====================================================================

        private static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return RxWhitespace.Replace(text, " ").Trim();
        }

        private static string NormalizeHtmlToPlainText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var t = input;

            // decode entities first (helps HtmlAgilityPack)
            if (t.Contains('&'))
                t = WebUtility.HtmlDecode(t);

            // HtmlAgilityPack (robust to broken HTML)
            try
            {
                var doc = new HtmlDocument
                {
                    OptionFixNestedTags = true,
                    OptionAutoCloseOnEnd = true
                };

                doc.LoadHtml(t);

                var plain = doc.DocumentNode?.InnerText ?? t;
                return NormalizeWhitespace(plain);
            }
            catch
            {
                // safe fallback
                return NormalizeWhitespace(t);
            }
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(normalizedString.Length);

            foreach (var c in normalizedString)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string NormalizeAllDashCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = new StringBuilder(text.Length);

            foreach (var c in text)
            {
                switch (c)
                {
                    case '\u002D':
                        result.Append('-');
                        break;

                    case '\u2010':
                    case '\u2011':
                    case '\u2012':
                    case '\u2013':
                    case '\u2014':
                    case '\u2015':
                    case '\u2053':
                    case '\u2E17':
                    case '\u2E1A':
                    case '\u2E3A':
                    case '\u2E3B':
                    case '\uFE58':
                    case '\uFE63':
                    case '\uFF0D':
                        result.Append('-');
                        break;

                    case '\u00AD':
                    case '\u1806':
                        break;

                    case '_':
                        result.Append(' ');
                        break;

                    case '~':
                        result.Append('-');
                        break;

                    default:
                        result.Append(c);
                        break;
                }
            }

            return result.ToString();
        }

        // =====================================================================
        // 5) Bilingual / Non-English Preservation
        // =====================================================================

        public static bool ShouldPreserveNonEnglishText(string? sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return false;

            return BilingualSources.Contains(sourceCode);
        }

        public static string PreserveBilingualContent(string text, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (!ShouldPreserveNonEnglishText(sourceCode))
                return text;

            // Use HtmlAgilityPack here as well (Collins/CHN sources may carry HTML fragments)
            return NormalizeHtmlToPlainText(text);
        }

        // =====================================================================
        // 6) Definition Normalization
        // =====================================================================

        public static string NormalizeDefinitionForSource(string definition, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            if (ShouldPreserveNonEnglishText(sourceCode))
                return PreserveBilingualContent(definition, sourceCode);

            return NormalizeDefinition(definition);
        }

        public static string NormalizeDefinition(string definition, string? sourceCode = null)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition;

            if (!string.IsNullOrWhiteSpace(sourceCode))
                return NormalizeDefinitionForSource(definition, sourceCode);

            // use HTML-safe normalization always (better than regex)
            return NormalizeHtmlToPlainText(definition);
        }

        // =====================================================================
        // 7) JSON Helpers
        // =====================================================================

        public static string? ExtractJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var value = property.GetString();
                return !string.IsNullOrWhiteSpace(value) ? value : null;
            }

            return null;
        }

        public static JsonElement.ArrayEnumerator? ExtractJsonArray(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray();
            }

            return null;
        }

        // =====================================================================
        // 8) Tokenization (Lightweight, no Lucene dependency)
        // =====================================================================

        public static IReadOnlyList<string> TokenizeWords(string? text, bool keepApostrophes = true)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            var t = NormalizeWhitespace(text);

            // Remove punctuation except apostrophe (optional)
            if (keepApostrophes)
            {
                t = Regex.Replace(t, @"[^\p{L}\p{N}\s'\-]", " ");
            }
            else
            {
                t = Regex.Replace(t, @"[^\p{L}\p{N}\s\-]", " ");
            }

            t = NormalizeWhitespace(t);

            if (t.Length == 0)
                return Array.Empty<string>();

            return t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        // =====================================================================
        // 9) Generic Text Checks
        // =====================================================================

        public static bool ContainsLanguageMarker(string text, params string[] languages)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (var language in languages)
            {
                if (text.Contains(language, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public static bool ContainsEnglishLetters(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && RxHasEnglishLetter.IsMatch(text);
        }

        // =====================================================================
        // 10) Source Processing Control
        // =====================================================================

        private static readonly ConcurrentDictionary<string, ProcessingState> _sourceProcessingState =
            new(StringComparer.OrdinalIgnoreCase);

        private sealed class ProcessingState
        {
            public int Count;
            public int LimitReachedLogged;
        }

        public static bool ShouldContinueProcessing(string sourceCode, ILogger? logger = null)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return false;

            var state = _sourceProcessingState.GetOrAdd(sourceCode, _ => new ProcessingState());

            if (Volatile.Read(ref state.Count) >= MAX_RECORDS_PER_SOURCE)
            {
                LogLimitOnce(logger, sourceCode);
                return false;
            }

            var newCount = Interlocked.Increment(ref state.Count);

            if (newCount > MAX_RECORDS_PER_SOURCE)
            {
                LogLimitOnce(logger, sourceCode);
                return false;
            }

            return true;
        }

        public static void ResetProcessingState(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return;

            _sourceProcessingState.TryRemove(sourceCode, out _);
        }

        public static int GetCurrentCount(string sourceCode)
        {
            return _sourceProcessingState.TryGetValue(sourceCode, out var state)
                ? state.Count
                : 0;
        }

        private static void LogLimitOnce(ILogger? logger, string sourceCode)
        {
            if (logger == null)
                return;

            var state = _sourceProcessingState.GetOrAdd(sourceCode, _ => new ProcessingState());

            if (Volatile.Read(ref state.LimitReachedLogged) != 0)
                return;

            if (Interlocked.Exchange(ref state.LimitReachedLogged, 1) != 0)
                return;

            logger.LogInformation(
                "Reached maximum of {MaxRecords} records for {Source} source",
                MAX_RECORDS_PER_SOURCE, sourceCode);
        }

        // =====================================================================
        // 11) Webster and General Parser
        // =====================================================================

        public static string? ExtractSection(string definition, string marker)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            if (string.IsNullOrWhiteSpace(marker))
                return null;

            var startIndex = definition.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
                return null;

            startIndex += marker.Length;

            var endIndex = definition.IndexOf("【", startIndex, StringComparison.Ordinal);
            if (endIndex < 0)
                endIndex = definition.Length;

            return definition.Substring(startIndex, endIndex - startIndex).Trim();
        }

        // =====================================================================
        // 12) Helper Creation
        // =====================================================================

        public static ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry?.Word ?? "unnamed sense",
                Definition = entry?.Definition ?? string.Empty,
                RawFragment = entry?.RawFragment ?? string.Empty,
                SenseNumber = entry?.SenseNumber ?? 1,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = new List<string>(),
                Alias = null
            };
        }

        // =====================================================================
        // 13) Logging and Error Handling
        // =====================================================================

        public static void LogProgress(ILogger logger, string sourceCode, int count)
        {
            if (logger == null)
                return;

            if (count % 10 != 0)
                return;

            logger.LogInformation(
                "{Source} processing progress: {Count} records processed",
                sourceCode, count);
        }

        public static void HandleError(ILogger logger, Exception ex, string sourceCode, string operation)
        {
            logger.LogError(ex, "Error {Operation} for {Source} entry", operation, sourceCode);
            ResetProcessingState(sourceCode);
        }

        // =====================================================================
        // 14) Domain Extraction
        // =====================================================================

        public static string? ExtractProperDomain(string sourceCode, string? rawDomain, string definition)
        {
            if (string.IsNullOrWhiteSpace(rawDomain))
                return null;

            var domain = rawDomain.Trim();

            switch (sourceCode)
            {
                case "ENG_OXFORD":
                    return ExtractOxfordDomain(definition);

                case "ENG_COLLINS":
                    return ExtractCollinsDomain(domain, definition);

                case "STRUCT_JSON":
                case "KAIKKI":
                    domain = RxKaikkiDomainStrip.Replace(domain, "").Trim();
                    return domain.Length <= 50 ? domain : domain.Substring(0, 50);

                case "GUT_WEBSTER":
                    return ExtractGutenbergDomain(domain);

                case "CENTURY21":
                    return null;

                case "ENG_CHN":
                    return ExtractChnDomain(definition);

                default:
                    return CleanDomainGeneric(domain);
            }
        }

        private static string? ExtractOxfordDomain(string definition)
        {
            var oxfordMatch = RxOxfordLeadingDomain.Match(definition ?? string.Empty);
            if (!oxfordMatch.Success)
                return null;

            var oxfordDomain = oxfordMatch.Groups[1].Value.Trim();
            oxfordDomain = oxfordDomain.Split('.')[0].Trim();

            return oxfordDomain.Length <= 100
                ? oxfordDomain
                : oxfordDomain.Substring(0, 100);
        }

        private static string? ExtractCollinsDomain(string domain, string definition)
        {
            if (domain.StartsWith("【语域标签】：", StringComparison.OrdinalIgnoreCase))
            {
                domain = domain.Substring("【语域标签】：".Length).Trim();

                var parts = domain.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    return parts[0].Trim();
            }

            if (!string.IsNullOrWhiteSpace(definition))
            {
                if (definition.Contains("主美") || definition.Contains("美式")) return "US";
                if (definition.Contains("主英") || definition.Contains("英式")) return "UK";
                if (definition.Contains("正式")) return "FORMAL";
                if (definition.Contains("非正式")) return "INFORMAL";
            }

            return null;
        }

        private static string? ExtractGutenbergDomain(string domain)
        {
            var gutenbergMatch = RxGutenbergDomain.Match(domain);
            if (!gutenbergMatch.Success)
                return null;

            return gutenbergMatch.Groups[1].Value.Trim().TrimEnd('.');
        }

        private static string? ExtractChnDomain(string definition)
        {
            var chnMatch = RxChnDomain.Match(definition ?? string.Empty);
            if (!chnMatch.Success)
                return null;

            return chnMatch.Groups[1].Value.Trim();
        }

        private static string? CleanDomainGeneric(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return null;

            domain = domain.Trim();

            if (domain.Length > 100)
                return null;

            if (DomainDefinitionIndicators.Any(ind => domain.Contains(ind, StringComparison.OrdinalIgnoreCase)))
                return null;

            domain = RxHasCjk.Replace(domain, "").Trim();

            return string.IsNullOrWhiteSpace(domain) ? null : domain;
        }

        // =====================================================================
        // 15) Headword Detection
        // =====================================================================

        public static bool IsHeadword(string line, int maxLength = 40, bool requireUppercase = true)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var text = line.Trim();

            if (text.Length > maxLength)
                return false;

            if (requireUppercase && !text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal))
                return false;

            if (!text.Any(char.IsLetter))
                return false;

            return true;
        }

        // =====================================================================
        // 16) Definition Cleaning helpers
        // =====================================================================

        public static string RemoveIpaMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return RxIpaSlashBlock.Replace(text, string.Empty);
        }

        public static string RemoveSyllableMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return RxEnglishOrthographicSyllableLine.Replace(text, string.Empty);
        }

        public static string RemovePosMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return RxLeadingPos.Replace(text, string.Empty);
        }

        public static string RemoveHeadwordFromDefinition(string definition, string headword)
        {
            if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(headword))
                return definition ?? string.Empty;

            var escapedHeadword = Regex.Escape(headword);

            return Regex.Replace(
                definition,
                @"^\s*" + escapedHeadword + @"\s+",
                string.Empty,
                RegexOptions.IgnoreCase);
        }

        public static string RemoveSeparators(string text, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var result = text;

            foreach (var separator in separators)
                result = result.Replace(separator.ToString(), string.Empty);

            return result;
        }

        public static string CleanDefinition(string definition, string? headword = null, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition ?? string.Empty;

            // BEST: HTML safe cleanup first, THEN other removals
            var cleaned = NormalizeHtmlToPlainText(definition);

            var hasBilingualMarkers =
                cleaned.Contains('【') || cleaned.Contains('】') ||
                cleaned.Contains('•') || cleaned.Contains('⬄');

            if (RxHasCjk.IsMatch(cleaned) || hasBilingualMarkers)
            {
                // preserve structure, just normalize whitespace
                cleaned = NormalizeWhitespace(cleaned);
            }
            else
            {
                cleaned = RemoveIpaMarkers(cleaned);
                cleaned = RemoveSyllableMarkers(cleaned);
                cleaned = RemovePosMarkers(cleaned);

                if (!string.IsNullOrWhiteSpace(headword))
                    cleaned = RemoveHeadwordFromDefinition(cleaned, headword);

                if (separators != null && separators.Length > 0)
                    cleaned = RemoveSeparators(cleaned, separators);

                cleaned = NormalizeWhitespace(cleaned);
            }

            return cleaned;
        }

        // =====================================================================
        // 17) Word Normalization
        // =====================================================================

        public static string NormalizeWordWithSourceContext(string word, string sourceCode)
        {
            return NormalizeWordPreservingLanguage(word, sourceCode);
        }

        public static string NormalizeWord(string? word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            var normalized = word.ToLowerInvariant();

            normalized = NormalizeAllDashCharacters(normalized);

            var formattingChars = new[] { "★", "☆", "●", "○", "▶", "【", "】" };
            foreach (var ch in formattingChars)
                normalized = normalized.Replace(ch, "");

            normalized = RemoveDiacritics(normalized);

            normalized = RxNonWordForNormalizedWord.Replace(normalized, " ");
            normalized = NormalizeWhitespace(normalized);

            return normalized;
        }

        public static string NormalizeWordPreservingLanguage(string? word, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            var normalized = word.Trim();

            normalized = NormalizeAllDashCharacters(normalized);

            if (ShouldPreserveNonEnglishText(sourceCode))
            {
                var formattingChars = new[] { "★", "☆", "●", "○", "▶" };
                foreach (var ch in formattingChars)
                    normalized = normalized.Replace(ch, "");

                return NormalizeWhitespace(normalized);
            }

            return NormalizeWord(normalized);
        }

        // =====================================================================
        // 18) POS Normalization
        // =====================================================================

        public static string NormalizePartOfSpeech(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos))
                return "unk";

            var normalized = pos.Trim().ToLowerInvariant();

            return normalized switch
            {
                "noun" or "n." or "n" => "noun",
                "verb" or "v." or "v" or "vi." or "vt." => "verb",
                "adjective" or "adj." or "adj" => "adj",
                "adverb" or "adv." or "adv" => "adv",
                "preposition" or "prep." or "prep" => "preposition",
                "pronoun" or "pron." or "pron" => "pronoun",
                "conjunction" or "conj." or "conj" => "conjunction",
                "interjection" or "interj." or "exclamation" => "exclamation",
                "determiner" or "det." => "determiner",
                "numeral" => "numeral",
                "article" => "determiner",
                "particle" => "particle",
                "phrase" => "phrase",
                "prefix" or "pref." => "prefix",
                "suffix" or "suf." => "suffix",
                "abbreviation" or "abbr." => "abbreviation",
                "symbol" => "symbol",
                _ => normalized.EndsWith('.') ? normalized[..^1] : normalized
            };
        }

        // =====================================================================
        // 19) Synonym Normalization
        // =====================================================================

        public static string NormalizeSynonymText(string? synonymText)
        {
            if (string.IsNullOrWhiteSpace(synonymText))
                return string.Empty;

            var t = synonymText.Trim();

            if (t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            t = NormalizeWhitespace(t);

            t = t.Trim('\"', '\'', '“', '”', '‘', '’', '.', ',', ';', ':', '!', '?');

            if (t.Length > 80)
                t = t.Substring(0, 80).Trim();

            if (!RxHasEnglishLetter.IsMatch(t))
                return string.Empty;

            return t;
        }

        // =====================================================================
        // 20) Simple Language Utilities
        // =====================================================================

        public static string LanguageDetect(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "en";

            foreach (var c in text)
                if (c >= '\u4E00' && c <= '\u9FFF')
                    return "zh";

            return "en";
        }

        public static string NormalizedWordSanitize(string input, string language)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            if (language == "zh")
                return input.Trim();

            var text = input.ToLowerInvariant();
            text = RxNoiseLettersOnly.Replace(text, " ");
            return NormalizeWhitespace(text);
        }

        public static bool IsCanonicalEligible(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            if (normalized.Contains(' '))
                return false;

            if (normalized.Contains('\''))
                return false;

            return true;
        }

        // =====================================================================
        // 21) Locale + IPA normalization
        // =====================================================================

        public static string NormalizeLocaleCode(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
                return string.Empty;

            var t = localeCode.Trim();

            t = t.Replace('_', '-');

            if (t.Length > 15)
                t = t.Substring(0, 15);

            return t;
        }

        public static string NormalizeIpa(string? ipa)
        {
            if (string.IsNullOrWhiteSpace(ipa))
                return string.Empty;

            var t = ipa.Trim();

            t = t.Replace("[[", "").Replace("]]", "");
            t = t.Replace("{{", "").Replace("}}", "");

            t = NormalizeWhitespace(t);

            if (t.Length > 300)
                t = t.Substring(0, 300).Trim();

            if (t.Length < 2)
                return string.Empty;

            return IpaNormalize(t);
        }

        public static string IpaNormalize(string ipa)
        {
            if (string.IsNullOrWhiteSpace(ipa))
                return ipa;

            ipa = ipa.Normalize(NormalizationForm.FormC);

            var sb = new StringBuilder(ipa.Length);

            foreach (var ch in ipa)
            {
                switch (ch)
                {
                    case ':':
                        sb.Append('ː');
                        break;

                    case '͡':
                        sb.Append('͜');
                        break;

                    case '.':
                    case ',':
                    case '，':
                        break;

                    case ' ':
                    case '\t':
                    case '\n':
                        sb.Append(' ');
                        break;

                    default:
                        sb.Append(ch);
                        break;
                }
            }
            return NormalizeWhitespace(sb.ToString());
        }
        // =====================================================================
        // 22) CJK Helpers / Strippers
        // =====================================================================
        public static class CjkPunctuationStripper
        {
            public static string RemoveCjkPunctuation(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return input;

                return RxCjkPunctuation.Replace(input, string.Empty).Trim();
            }
        }
        public static class CjkStripper
        {
            public static string RemoveCjk(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return input;

                return RxCjkBlocks.Replace(input, string.Empty).Trim();
            }
        }

        internal static class DomainMarkerStripper
        {
            public static string Strip(string word)
            {
                if (string.IsNullOrWhiteSpace(word))
                    return word;

                return RxDomainMarkerStrip.Replace(word, "").Trim();
            }
        }
        // =====================================================================
        // 23) Generic IPA Extraction + Locale detection
        // =====================================================================
        internal static class GenericIpaExtractor
        {
            public static IReadOnlyDictionary<string, string> ExtractIpaWithLocale(string? text)
            {
                var result = new Dictionary<string, string>();

                if (string.IsNullOrWhiteSpace(text))
                    return result;

                var slashMatches = RxIpaSlashCore.Matches(text);

                var candidates =
                    slashMatches.Count > 0
                        ? slashMatches.Select(m => m.Groups[1].Value)
                        : new[] { text };

                foreach (var raw in candidates)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    if (RxIpaReject.IsMatch(raw))
                        continue;

                    if (RxIpaProseReject.IsMatch(raw))
                        continue;

                    if (!RxIpaPresence.IsMatch(raw))
                        continue;

                    var cleaned = raw;

                    cleaned = cleaned.Replace(":", "ː");
                    cleaned = RxIpaEditorialPunctuation.Replace(cleaned, "");
                    cleaned = RxParen.Replace(cleaned, "");
                    cleaned = RxIpaAllowedChars.Replace(cleaned, "");
                    cleaned = NormalizeWhitespace(cleaned);

                    if (cleaned.Length == 0)
                        continue;

                    var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var part in parts)
                    {
                        var ipaCore = RxEdgeHyphen.Replace(part.Trim(), "");

                        if (ipaCore.Length == 0)
                            continue;

                        if (!RxIpaPresence.IsMatch(ipaCore))
                            continue;

                        var canonicalIpa =
                            IpaAutoStressNormalizer.Normalize($"/{ipaCore}/");

                        if (result.ContainsKey(canonicalIpa))
                            continue;

                        var detectedLocale =
                            IpaLocaleDetector.Detect(ipaCore);

                        var systemLocale =
                            IpaLocaleDetector.MapToSystemLocale(detectedLocale);

                        if (!string.IsNullOrWhiteSpace(systemLocale))
                            result.Add(canonicalIpa, systemLocale);
                    }
                }
                return result;
            }
            public static string RemoveAll(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return text;

                return RxIpaSlashCore.Replace(text, "").Trim();
            }
        }
        internal static class IpaAutoStressNormalizer
        {
            public static string Normalize(string ipaWithSlashes)
            {
                if (string.IsNullOrWhiteSpace(ipaWithSlashes))
                    return ipaWithSlashes;

                var core = ipaWithSlashes.Trim('/');

                if (RxIpaStress.IsMatch(core))
                    return ipaWithSlashes;

                var vowelCount = RxIpaVowelForStressInjection.Matches(core).Count;
                if (vowelCount < 2)
                    return ipaWithSlashes;

                return $"/ˈ{core}/";
            }
        }
        internal static class IpaLocaleDetector
        {
            public static string Detect(string ipa)
            {
                if (string.IsNullOrWhiteSpace(ipa))
                    return "en";

                var usScore = 0;
                var gbScore = 0;

                if (RxIpaAmericanMarkers.IsMatch(ipa))
                    usScore++;

                if (RxIpaBritishMarkers.IsMatch(ipa))
                    gbScore++;

                if (ipa.Contains("ɚ") || ipa.Contains("ɝ"))
                    usScore += 2;

                if (ipa.Contains("ː"))
                    gbScore += 2;

                if (usScore > gbScore)
                    return "en-US";

                if (gbScore > usScore)
                    return "en-GB";

                return "en";
            }

            public static string MapToSystemLocale(string detectedLocale)
            {
                return detectedLocale switch
                {
                    "en-GB" => "en-UK",
                    _ => detectedLocale
                };
            }
        }

        // =====================================================================
        // 24) IPA Syllabification + Rendering
        // =====================================================================
        internal static class IpaSyllablePostProcessor
        {
            public static IReadOnlyList<IpaSyllable> Normalize(IReadOnlyList<IpaSyllable> syllables)
            {
                if (syllables == null || syllables.Count == 0)
                    return syllables;

                var buffer = new List<IpaSyllable>();

                foreach (var current in syllables)
                {
                    if (buffer.Count == 0)
                    {
                        buffer.Add(current);
                        continue;
                    }

                    var hasVowel = RxIpaSyllableVowel.IsMatch(current.Text);
                    var hasConsonant = RxIpaSyllableConsonant.IsMatch(current.Text);

                    if (!hasVowel || !hasConsonant)
                    {
                        var prev = buffer[^1];

                        buffer[^1] = new IpaSyllable(
                            prev.Index,
                            prev.Text + current.Text,
                            Math.Max(prev.StressLevel, current.StressLevel));
                    }
                    else
                    {
                        buffer.Add(current);
                    }
                }

                var result = new List<IpaSyllable>(buffer.Count);
                var index = 1;

                foreach (var s in buffer)
                    result.Add(new IpaSyllable(index++, s.Text, s.StressLevel));

                return result;
            }
        }
        internal static class IpaSyllabifier
        {
            private static readonly HashSet<char> Vowels =
            [
                'i', 'y', 'ɪ', 'ʏ', 'e', 'ø', 'ɛ', 'œ', 'æ', 'a', 'ɑ', 'ɒ', 'ɔ', 'o', 'ʊ', 'u',
                'ə', 'ɚ', 'ɝ', 'ɜ', 'ɵ', 'ɐ'
            ];

            public static IReadOnlyList<IpaSyllable> Split(string ipa)
            {
                var result = new List<IpaSyllable>();

                if (string.IsNullOrWhiteSpace(ipa))
                    return result;

                var buffer = new StringBuilder();
                var hasVowel = false;
                byte currentStress = 0;
                var index = 1;

                foreach (var ch in ipa)
                {
                    if (ch == 'ˈ')
                    {
                        currentStress = 2;
                        continue;
                    }

                    if (ch == 'ˌ')
                    {
                        currentStress = 1;
                        continue;
                    }

                    buffer.Append(ch);

                    if (Vowels.Contains(ch))
                    {
                        if (hasVowel)
                        {
                            result.Add(new IpaSyllable(
                                index++,
                                buffer.ToString(0, buffer.Length - 1),
                                currentStress));

                            buffer.Clear();
                            buffer.Append(ch);
                            currentStress = 0;
                        }

                        hasVowel = true;
                    }
                }

                if (buffer.Length > 0)
                    result.Add(new IpaSyllable(index, buffer.ToString(), currentStress));

                return result;
            }
        }

        internal static class IpaStressRenderer
        {
            public static string Render(
                IReadOnlyList<IpaSyllable> syllables,
                IpaStressRenderProfile profile)
            {
                if (syllables == null || syllables.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();
                var useDots = profile == IpaStressRenderProfile.EnUk;

                sb.Append('/');

                for (var i = 0; i < syllables.Count; i++)
                {
                    var s = syllables[i];

                    if (i > 0 && useDots)
                        sb.Append('.');

                    if (s.StressLevel == 2) sb.Append('ˈ');
                    else if (s.StressLevel == 1) sb.Append('ˌ');

                    sb.Append(s.Text);
                }

                sb.Append('/');

                return sb.ToString();
            }
        }

        // =====================================================================
        // 25) Orthographic Syllables
        // =====================================================================

        public static class OrthographicSyllableExtractor
        {
            public static IReadOnlyList<string> Extract(string word)
            {
                var result = new List<string>();

                if (string.IsNullOrWhiteSpace(word))
                    return result;

                word = word.Trim();
                word = RxWordSanitizer.Replace(word, "");

                if (string.IsNullOrWhiteSpace(word))
                    return result;

                if (word.Contains('-'))
                {
                    var parts = word.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                        result.AddRange(Extract(part));

                    return result.Count == 0 ? new[] { word } : result;
                }

                if (word.Length <= 3)
                    return new[] { word };

                var vowelGroups = GetVowelGroups(word);
                if (vowelGroups.Count <= 1)
                    return new[] { word };

                var suffixCut = TryFindSuffixCut(word);
                if (suffixCut > 0 && suffixCut < word.Length - 1)
                {
                    var left = word.Substring(0, suffixCut);
                    var right = word.Substring(suffixCut);

                    if (HasVowel(left) && HasVowel(right))
                    {
                        var leftSyl = Extract(left).ToList();
                        var rightSyl = Extract(right).ToList();

                        leftSyl.AddRange(rightSyl);
                        return NormalizeFinalSylList(leftSyl);
                    }
                }

                var cuts = new List<int>();

                for (var g = 0; g < vowelGroups.Count - 1; g++)
                {
                    var leftVowel = vowelGroups[g];
                    var rightVowel = vowelGroups[g + 1];

                    var betweenStart = leftVowel.End + 1;
                    var betweenEnd = rightVowel.Start - 1;

                    if (betweenStart > betweenEnd)
                        continue;

                    var cluster = word.Substring(betweenStart, betweenEnd - betweenStart + 1);

                    var cutIndex = ChooseCutIndex(betweenStart, cluster);

                    if (cutIndex > 0 && cutIndex < word.Length)
                        cuts.Add(cutIndex);
                }

                if (cuts.Count == 0)
                    return new[] { word };

                cuts = cuts.Distinct().OrderBy(x => x).ToList();

                var last = 0;

                foreach (var cut in cuts)
                {
                    if (cut <= last)
                        continue;

                    var part = word.Substring(last, cut - last);
                    if (!string.IsNullOrWhiteSpace(part))
                        result.Add(part);

                    last = cut;
                }

                if (last < word.Length)
                    result.Add(word.Substring(last));

                return NormalizeFinalSylList(result);
            }

            private sealed class VowelGroup
            {
                public int Start { get; init; }
                public int End { get; init; }
            }

            private static List<VowelGroup> GetVowelGroups(string word)
            {
                var groups = new List<VowelGroup>();

                var i = 0;
                while (i < word.Length)
                {
                    if (!IsVowel(word[i]))
                    {
                        i++;
                        continue;
                    }

                    var start = i;
                    var end = i;

                    while (end + 1 < word.Length && IsVowel(word[end + 1]))
                        end++;

                    groups.Add(new VowelGroup { Start = start, End = end });
                    i = end + 1;
                }

                return groups;
            }

            private static bool IsVowel(char c)
            {
                return RxOrthographicVowel.IsMatch(c.ToString());
            }

            private static bool HasVowel(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                foreach (var ch in text)
                    if (IsVowel(ch))
                        return true;

                return false;
            }

            private static int TryFindSuffixCut(string word)
            {
                foreach (var suffix in OrthographicStrongSuffixes)
                {
                    if (word.Length <= suffix.Length + 2)
                        continue;

                    if (word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return word.Length - suffix.Length;
                }

                return -1;
            }

            private static int ChooseCutIndex(int betweenStart, string cluster)
            {
                if (cluster.Length == 1)
                    return betweenStart;

                if (cluster.Length == 2)
                {
                    if (OrthographicDigraphConsonants.Contains(cluster))
                        return betweenStart;

                    return betweenStart + 1;
                }

                if (cluster.Length >= 3)
                {
                    var first2 = cluster.Substring(0, 2);
                    if (OrthographicDigraphConsonants.Contains(first2))
                        return betweenStart;

                    return betweenStart + 1;
                }

                return betweenStart;
            }

            private static IReadOnlyList<string> NormalizeFinalSylList(List<string> syllables)
            {
                if (syllables == null || syllables.Count == 0)
                    return Array.Empty<string>();

                for (var i = 0; i < syllables.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(syllables[i]))
                    {
                        syllables.RemoveAt(i);
                        i--;
                    }
                }

                if (syllables.Count <= 1)
                    return syllables;

                for (var i = 0; i < syllables.Count - 1; i++)
                {
                    if (syllables[i].Length == 1)
                    {
                        syllables[i + 1] = syllables[i] + syllables[i + 1];
                        syllables.RemoveAt(i);
                        i--;
                    }
                }

                return syllables;
            }
        }

        public static class OrthographicSyllableRenderer
        {
            public static string Render(IReadOnlyList<string> syllables)
            {
                return syllables == null || syllables.Count == 0
                    ? string.Empty
                    : string.Join("·", syllables);
            }
        }

        // =====================================================================
        // 26) Language Detector (NTextCat-backed) + fallback to existing service
        // =====================================================================
        public static class LanguageDetector
        {
            private static readonly object _lock = new();

            private static DictionaryImporter.Gateway.Grammar.Core.ILanguageDetector? _detector;

            private static DictionaryImporter.Gateway.Grammar.Core.ILanguageDetector GetDetector()
            {
                if (_detector != null)
                    return _detector;

                lock (_lock)
                {
                    if (_detector != null)
                        return _detector;

                    try
                    {
                        _detector = new DictionaryImporter.Gateway.Grammar.Engines.LanguageDetector();
                    }
                    catch
                    {
                        _detector = null;
                    }

                    return _detector!;
                }
            }

            public static string DetectLanguageCode(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return "en-US";

                // Fast shortcut: Chinese detection (instant)
                foreach (var c in text)
                {
                    if (c >= '\u4E00' && c <= '\u9FFF')
                        return "zh-CN";
                }

                try
                {
                    var detector = GetDetector();
                    if (detector == null)
                        return "en-US";

                    var code = detector.Detect(text);

                    return string.IsNullOrWhiteSpace(code) ? "en-US" : code.Trim();
                }
                catch
                {
                    return "en-US";
                }
            }

            public static bool ContainsNonEnglishText(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                var code = DetectLanguageCode(text);

                // treat only "en-*" as English
                return !code.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            }

            public static bool IsBilingualText(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                // Safe best-effort bilingual rule:
                // if contains CJK + English letters => bilingual
                var hasCjk = false;
                foreach (var c in text)
                {
                    if (c >= '\u4E00' && c <= '\u9FFF')
                    {
                        hasCjk = true;
                        break;
                    }
                }

                if (!hasCjk)
                    return false;

                return Regex.IsMatch(text, @"[A-Za-z]");
            }
        }
    }
}
