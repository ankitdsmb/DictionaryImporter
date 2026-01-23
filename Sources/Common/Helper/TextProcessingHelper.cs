using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Provides shared text processing utilities for dictionary sources.
    /// </summary>
    public static class TextProcessingHelper
    {
        #region Regex Patterns

        private static readonly Regex HasEnglishLetter = new("[A-Za-z]", RegexOptions.Compiled);
        private static readonly Regex IpaRegex = new(@"/[^/]+/", RegexOptions.Compiled);
        private static readonly Regex EnglishSyllableRegex = new(@"^\s*[A-Za-z]+(?:·[A-Za-z]+)+\s*", RegexOptions.Compiled);

        private static readonly Regex PosRegex = new(
            @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PosOnlyRegex = new(
            @"^\s*(n\.|v\.|a\.|adj\.|ad\.|adv\.|vt\.|vi\.|abbr\.)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion Regex Patterns

        #region Headword Detection

        /// <summary>
        /// Determines if a line contains a dictionary headword.
        /// </summary>
        public static bool IsHeadword(string line, int maxLength = 40, bool requireUppercase = true)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            var text = line.Trim();

            // Check length limit
            if (text.Length > maxLength) return false;

            // Check uppercase requirement (for Gutenberg-style dictionaries)
            if (requireUppercase && !text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal))
                return false;

            // Must contain at least one letter
            if (!text.Any(char.IsLetter)) return false;

            return true;
        }

        /// <summary>
        /// Checks if text contains English letters.
        /// </summary>
        public static bool ContainsEnglishLetters(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && HasEnglishLetter.IsMatch(text);
        }

        #endregion Headword Detection

        #region Text Cleaning and Normalization

        /// <summary>
        /// Removes IPA pronunciation markers from text.
        /// </summary>
        public static string RemoveIpaMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return IpaRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// Removes English syllable markers from text.
        /// </summary>
        public static string RemoveSyllableMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return EnglishSyllableRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// Removes part-of-speech markers from the beginning of text.
        /// </summary>
        public static string RemovePosMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return PosRegex.Replace(text, string.Empty);
        }

        /// <summary>
        /// Removes the headword from the beginning of definition text.
        /// </summary>
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

        /// <summary>
        /// Removes separator characters from text.
        /// </summary>
        public static string RemoveSeparators(string text, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = text;
            foreach (var separator in separators)
            {
                result = result.Replace(separator.ToString(), string.Empty);
            }
            return result;
        }

        /// <summary>
        /// Normalizes whitespace in text.
        /// </summary>
        public static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Cleans definition text by removing common markers and normalizing.
        /// </summary>
        public static string CleanDefinition(string definition, string? headword = null, params char[] separators)
        {
            if (string.IsNullOrWhiteSpace(definition)) return definition ?? string.Empty;
            var cleaned = definition;

            // Check if this contains Chinese characters or bilingual markers
            bool hasChineseChars = Regex.IsMatch(definition, @"[\u4E00-\u9FFF]");
            bool hasBilingualMarkers = definition.Contains('【') || definition.Contains('】') ||
                                       definition.Contains('•') || definition.Contains('⬄');

            if (hasChineseChars || hasBilingualMarkers)
            {
                // For bilingual content: ONLY remove HTML tags and normalize whitespace
                // DO NOT remove any content characters
                cleaned = Regex.Replace(cleaned, @"<[^>]+>", " ");
                cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
                return cleaned;
            }

            // Original logic for pure English text
            cleaned = RemoveIpaMarkers(cleaned);
            cleaned = RemoveSyllableMarkers(cleaned);
            cleaned = RemovePosMarkers(cleaned);

            if (!string.IsNullOrWhiteSpace(headword))
                cleaned = RemoveHeadwordFromDefinition(cleaned, headword);

            if (separators.Length > 0)
                cleaned = RemoveSeparators(cleaned, separators);

            cleaned = NormalizeWhitespace(cleaned);
            return cleaned;
        }

        #endregion Text Cleaning and Normalization

        public static string NormalizeWord(string? word)
        {
            if (string.IsNullOrWhiteSpace(word)) return string.Empty;
            var normalized = word.ToLowerInvariant();
            // FIX: Normalize ALL dash/hyphen characters to regular hyphen U+002D
            normalized = NormalizeAllDashCharacters(normalized);
            // Remove formatting characters
            var formattingChars = new[] { "★", "☆", "●", "○", "▶", "【", "】" };
            foreach (var ch in formattingChars)
            {
                normalized = normalized.Replace(ch, "");
            }
            // Remove diacritics
            normalized = RemoveDiacritics(normalized);
            // Remove non-letter/number characters (except hyphen and apostrophe)
            // After normalization, all dashes are U+002D, so they'll be preserved
            normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}\s\-']", " ");
            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            return normalized;
        }

        private static string NormalizeAllDashCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                // Map ALL dash/hyphen-like characters appropriately
                switch (c)
                {
                    // Regular hyphen - keep as hyphen
                    case '\u002D': // HYPHEN-MINUS
                        result.Append('-');
                        break;
                    // Dash characters that should become hyphen
                    case '\u2010': // HYPHEN
                    case '\u2011': // NON-BREAKING HYPHEN
                    case '\u2012': // FIGURE DASH
                    case '\u2013': // EN DASH
                    case '\u2014': // EM DASH
                    case '\u2015': // HORIZONTAL BAR
                    case '\u2053': // SWUNG DASH
                    case '\u2E17': // DOUBLE OBLIQUE HYPHEN
                    case '\u2E1A': // HYPHEN WITH DIAERESIS
                    case '\u2E3A': // TWO-EM DASH
                    case '\u2E3B': // THREE-EM DASH
                    case '\uFE58': // SMALL EM DASH
                    case '\uFE63': // SMALL HYPHEN-MINUS
                    case '\uFF0D': // FULLWIDTH HYPHEN-MINUS
                        result.Append('-'); // U+002D
                        break;
                    // SOFT HYPHEN and similar - should be REMOVED (not space)
                    case '\u00AD': // SOFT HYPHEN
                    case '\u1806': // MONGOLIAN TODO SOFT HYPHEN
                        // Don't append anything - remove it completely
                        break;
                    // Underscore - treat as space
                    case '_':
                        result.Append(' ');
                        break;
                    // Tilde sometimes used as dash substitute
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

        // Ensure RemoveDiacritics handles all cases
        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();
            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        // FIXED: This was breaking bilingual sources by removing Chinese characters
        public static string NormalizeWordPreservingLanguage(string? word, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(word)) return string.Empty;

            var normalized = word.ToLowerInvariant().Trim();
            // Apply dash normalization for ALL sources
            normalized = NormalizeAllDashCharacters(normalized);

            // FIX: For bilingual sources, ONLY remove formatting characters, do NOT call NormalizeWord()
            var bilingualSources = new HashSet<string>
            {
                "ENG_CHN", "CENTURY21", "ENG_OXFORD", "ENG_COLLINS"
            };

            if (bilingualSources.Contains(sourceCode))
            {
                // Only remove specific formatting characters
                var formattingChars = new[] { "★", "☆", "●", "○", "▶" };
                foreach (var ch in formattingChars)
                {
                    normalized = normalized.Replace(ch, "");
                }
                return normalized.Trim();
            }

            // For Latin-based sources, use full normalization
            return NormalizeWord(normalized);
        }

        public static string NormalizePartOfSpeech(string? pos)
        {
            if (string.IsNullOrWhiteSpace(pos)) return "unk";
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
    }
}