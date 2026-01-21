using System;
using System.Collections.Generic;
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
    }
}