using System;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Core.Rewrite
{
    public sealed class DictionaryHumanizer
    {
        public string HumanizeDefinition(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Trim();

            text = NormalizeWhitespace(text);
            text = NormalizePunctuationSpacing(text);

            // Meaning-safe sentence casing:
            // Only uppercase first alpha char IF the definition looks like plain text,
            // and do NOT touch if it contains special formatting / abbreviations patterns.
            if (ShouldApplySentenceCase(text))
            {
                text = UppercaseFirstLetter(text);
            }

            // Meaning-safe trailing dot normalization:
            // If it ends with ';' or ',' replace with '.'
            text = NormalizeEndingPunctuation(text);

            return text.Trim();
        }

        public string HumanizeExample(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Trim();

            text = NormalizeWhitespace(text);
            text = NormalizePunctuationSpacing(text);

            if (ShouldApplySentenceCase(text))
            {
                text = UppercaseFirstLetter(text);
            }

            text = NormalizeEndingPunctuation(text);

            return text.Trim();
        }

        public string HumanizeTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Titles should remain conservative to avoid meaning loss:
            // no forced casing, only whitespace/punctuation spacing.
            text = text.Trim();
            text = NormalizeWhitespace(text);
            text = NormalizePunctuationSpacing(text);

            return text.Trim();
        }

        // NEW METHOD (added)
        private static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Replace("\t", " ");
            text = Regex.Replace(text, @"\s{2,}", " ");
            text = Regex.Replace(text, @"\s+\n", "\n");
            text = Regex.Replace(text, @"\n\s+", "\n");

            return text.Trim();
        }

        // NEW METHOD (added)
        private static string NormalizePunctuationSpacing(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Remove space before punctuation.
            text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");

            // Ensure one space after punctuation when followed by letter/number (not newline)
            text = Regex.Replace(text, @"([,.;:!?])([A-Za-z0-9])", "$1 $2");

            // Fix multiple punctuation spacing issues: "word . " -> "word."
            text = Regex.Replace(text, @"\s+([.])", "$1");

            // Collapse duplicated punctuation like ".." -> "."
            // (but keep ellipsis "..." intact)
            text = Regex.Replace(text, @"(?<!\.)\.\.(?!\.)", ".");

            return text.Trim();
        }

        // NEW METHOD (added)
        private static bool ShouldApplySentenceCase(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Avoid casing change if definition contains:
            // 1) phonetics / IPA slash blocks
            // 2) lots of symbols or formatting markers
            // 3) already looks like a heading / all caps abbreviation
            if (text.Contains(" /") && text.Contains("/ "))
                return false;

            if (Regex.IsMatch(text, @"\[[^\]]+\]"))
                return false;

            if (Regex.IsMatch(text, @"\b[A-Z]{3,}\b"))
                return false;

            // If starts with non-letter, skip
            var firstLetterIndex = FirstLetterIndex(text);
            if (firstLetterIndex < 0)
                return false;

            // If already starts with uppercase, fine (no harm)
            return true;
        }

        // NEW METHOD (added)
        private static int FirstLetterIndex(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsLetter(ch))
                    return i;
            }

            return -1;
        }

        // NEW METHOD (added)
        private static string UppercaseFirstLetter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var idx = FirstLetterIndex(text);
            if (idx < 0)
                return text;

            var ch = text[idx];
            var upper = char.ToUpperInvariant(ch);

            if (ch == upper)
                return text;

            return text.Substring(0, idx) + upper + text.Substring(idx + 1);
        }

        // NEW METHOD (added)
        private static string NormalizeEndingPunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            text = text.Trim();

            // Do not force punctuation if ends with:
            // 1) quote
            // 2) closing bracket/paren
            // 3) colon (often used for lists)
            if (text.EndsWith("\"", StringComparison.Ordinal) ||
                text.EndsWith("'", StringComparison.Ordinal) ||
                text.EndsWith(")", StringComparison.Ordinal) ||
                text.EndsWith("]", StringComparison.Ordinal) ||
                text.EndsWith(":", StringComparison.Ordinal))
            {
                return text;
            }

            if (text.EndsWith(";", StringComparison.Ordinal) || text.EndsWith(",", StringComparison.Ordinal))
            {
                return text.Substring(0, text.Length - 1).TrimEnd() + ".";
            }

            return text;
        }
    }
}
