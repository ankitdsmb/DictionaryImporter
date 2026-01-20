using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Common.Helper
{
    public static class SourceAwareNormalizer
    {
        public static string NormalizeForSource(string text, string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            return sourceCode switch
            {
                "ENG_CHN" => NormalizeChineseEnglishText(text),
                "CENTURY21" => NormalizeBilingualText(text),
                "ENG_COLLINS" => NormalizeCollinsText(text),
                "ENG_OXFORD" => NormalizeBilingualText(text), // FIX: Oxford should also use bilingual normalization
                "GUT_WEBSTER" => NormalizeGutenbergText(text),
                _ => NormalizeGenericText(text)
            };
        }

        private static string NormalizeChineseEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Step 1: Remove the ⬄ separator if present
            var normalized = text;
            var separatorIdx = normalized.IndexOf('⬄');
            if (separatorIdx >= 0)
            {
                // Remove the separator and everything before it
                normalized = normalized.Substring(separatorIdx + 1).Trim();
            }

            // Step 2: Clean up whitespace
            normalized = normalized
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            // Step 3: Preserve ALL printable characters
            var result = new StringBuilder();
            foreach (char c in normalized)
            {
                // Keep all printable characters except controls
                if (!char.IsControl(c) && c != '\uFEFF')
                {
                    result.Append(c);
                }
            }

            // Step 4: Normalize whitespace
            normalized = Regex.Replace(result.ToString(), @"\s+", " ").Trim();
            return normalized;
        }

        private static string NormalizeBilingualText(string text)
        {
            // Preserve bilingual content (Chinese + English)
            var normalized = text
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            // Remove only control characters, keep ALL other characters
            var result = new StringBuilder();
            foreach (char c in normalized)
            {
                if (!char.IsControl(c))
                {
                    result.Append(c);
                }
            }

            normalized = Regex.Replace(result.ToString(), @"\s+", " ").Trim();
            return normalized;
        }

        private static string NormalizeCollinsText(string text)
        {
            // Collins-specific normalization
            var normalized = text
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            // Keep standard English text with accented characters
            var result = new StringBuilder();
            foreach (char c in normalized)
            {
                // Keep letters, digits, punctuation, and common symbols
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) ||
                    c == ' ' || c == '\t' || c == '–' || c == '—' || c == '‐' || c == '‑' || // various dashes
                    c == '·' || c == '•' || c == '°' || // bullets, degrees
                    (c >= 0xC0 && c <= 0xFF && c != 0xD7 && c != 0xF7)) // Latin-1 Supplement (with accents)
                {
                    result.Append(c);
                }
                else if (char.IsControl(c))
                {
                    // Skip control characters
                }
                else
                {
                    // For other characters, preserve them (including Chinese)
                    result.Append(c);
                }
            }

            normalized = Regex.Replace(result.ToString(), @"\s+", " ").Trim();
            return normalized;
        }

        private static string NormalizeGutenbergText(string text)
        {
            // Gutenberg-specific normalization
            var normalized = text;

            // Remove Gutenberg markers
            normalized = Regex.Replace(normalized, @"\*\*\*\s*START OF.*?\*\*\*", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\*\*\*\s*END OF.*?\*\*\*", "", RegexOptions.IgnoreCase);

            normalized = normalized
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            // Keep standard English text with accented characters
            var result = new StringBuilder();
            foreach (char c in normalized)
            {
                // Keep all printable characters including accented ones
                if (!char.IsControl(c) && c != '\uFEFF')
                {
                    result.Append(c);
                }
            }

            normalized = Regex.Replace(result.ToString(), @"\s+", " ").Trim();
            return normalized;
        }

        private static string NormalizeGenericText(string text)
        {
            // Original logic for English-only sources
            var normalized = text
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            // Keep all printable characters
            var result = new StringBuilder();
            foreach (char c in normalized)
            {
                if (!char.IsControl(c) && c != '\uFEFF')
                {
                    result.Append(c);
                }
            }

            normalized = Regex.Replace(result.ToString(), @"\s+", " ").Trim();
            return normalized;
        }
    }
}