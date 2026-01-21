// File: Core/Text/LanguageDetector.cs
using System.Text.RegularExpressions;

namespace DictionaryImporter.Core.Text
{
    public static class LanguageDetector
    {
        // IPA characters that should NOT be treated as non-English
        private static readonly Regex IpaRegex = new(@"[\/\[\]ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃]", RegexOptions.Compiled);

        // Unicode ranges for non-English scripts
        private static readonly (int Start, int End, string LanguageCode)[] NonEnglishRanges =
        {
            // Chinese
            (0x4E00, 0x9FFF, "zh"),    // CJK Unified Ideographs
            (0x3400, 0x4DBF, "zh"),    // CJK Extension A
            (0xF900, 0xFAFF, "zh"),    // CJK Compatibility Ideographs

            // Japanese
            (0x3040, 0x309F, "ja"),    // Hiragana
            (0x30A0, 0x30FF, "ja"),    // Katakana
            (0xFF00, 0xFFEF, "ja"),    // Halfwidth and Fullwidth Forms

            // Korean
            (0xAC00, 0xD7AF, "ko"),    // Hangul Syllables

            // Cyrillic
            (0x0400, 0x04FF, "ru"),    // Cyrillic
            (0x0500, 0x052F, "ru"),    // Cyrillic Supplement

            // Arabic
            (0x0600, 0x06FF, "ar"),    // Arabic
            (0x0750, 0x077F, "ar"),    // Arabic Supplement

            // Indian scripts
            (0x0900, 0x097F, "hi"),    // Devanagari
            (0x0980, 0x09FF, "bn"),    // Bengali
            (0x0A00, 0x0A7F, "pa"),    // Gurmukhi

            // Hebrew
            (0x0590, 0x05FF, "he"),    // Hebrew

            // Greek
            (0x0370, 0x03FF, "el"),    // Greek and Coptic
        };

        public static bool ContainsNonEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Remove IPA characters first
            var withoutIpa = IpaRegex.Replace(text, "");

            foreach (char c in withoutIpa)
            {
                int codePoint = (int)c;
                foreach (var range in NonEnglishRanges)
                {
                    if (codePoint >= range.Start && codePoint <= range.End)
                        return true;
                }
            }

            return false;
        }

        public static string? DetectLanguageCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Count characters per language
            var languageCounts = new Dictionary<string, int>();

            foreach (char c in text)
            {
                int codePoint = (int)c;
                foreach (var range in NonEnglishRanges)
                {
                    if (codePoint >= range.Start && codePoint <= range.End)
                    {
                        if (languageCounts.ContainsKey(range.LanguageCode))
                            languageCounts[range.LanguageCode]++;
                        else
                            languageCounts[range.LanguageCode] = 1;
                        break;
                    }
                }
            }

            // Return language with highest count
            return languageCounts.OrderByDescending(kv => kv.Value)
                               .Select(kv => kv.Key)
                               .FirstOrDefault();
        }

        // Special handling for ENG_CHN format
        public static bool ContainsChineseText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (char c in text)
            {
                int codePoint = (int)c;
                if ((codePoint >= 0x4E00 && codePoint <= 0x9FFF) ||
                    (codePoint >= 0x3400 && codePoint <= 0x4DBF) ||
                    (codePoint >= 0xF900 && codePoint <= 0xFAFF))
                {
                    return true;
                }
            }

            return false;
        }

        // Check if text is bilingual (English + other language)
        public static bool IsBilingualText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasEnglish = false;
            bool hasNonEnglish = false;

            foreach (char c in text)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    hasEnglish = true;

                int codePoint = (int)c;
                foreach (var range in NonEnglishRanges)
                {
                    if (codePoint >= range.Start && codePoint <= range.End)
                    {
                        hasNonEnglish = true;
                        break;
                    }
                }

                if (hasEnglish && hasNonEnglish)
                    return true;
            }

            return false;
        }
    }
}