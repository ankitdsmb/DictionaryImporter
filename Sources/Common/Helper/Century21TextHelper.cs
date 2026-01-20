using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace DictionaryImporter.Sources.Common.Helper
{
    public static class Century21TextHelper
    {
        // Pre-compiled regex for performance
        private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex ChineseCharRegex = new(@"[\u4E00-\u9FFF\u3400-\u4DBF]", RegexOptions.Compiled);
        private static readonly Regex FullChineseCharRegex = new(@"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3000-\u303F\uff00-\uffef]", RegexOptions.Compiled);
        private static readonly Regex IpaCharRegex = new(@"[\/\[\]ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃]", RegexOptions.Compiled);

        // Pre-compiled patterns for inference
        private static readonly Regex MeansRegex = new(@"(\w+)\s+means\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IsRegex = new(@"(\w+)\s+is\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RefersToRegex = new(@"(\w+)\s+refers to\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string CleanEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // Decode HTML entities once
            text = DecodeHtmlEntities(text);

            // Remove HTML tags but keep ALL text (English + Chinese)
            text = HtmlTagRegex.Replace(text, string.Empty);

            // Normalize whitespace but preserve all characters
            text = WhitespaceRegex.Replace(text, " ").Trim();

            return text;
        }

        public static bool ContainsChineseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return ChineseCharRegex.IsMatch(text);
        }

        public static string RemoveChineseCharacters(string text)
        {
            // ⚠️ DANGER: This strips Chinese from bilingual content
            // Only use for English-only sources like Gutenberg
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Add debug logging in DEBUG mode
#if DEBUG
            var stackTrace = new System.Diagnostics.StackTrace();
            var caller = stackTrace.GetFrame(1)?.GetMethod()?.Name;
            System.Diagnostics.Debug.WriteLine($"WARNING: RemoveChineseCharacters called by {caller}");
#endif

            return FullChineseCharRegex.Replace(text, string.Empty);
        }

        public static string RemoveChineseMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Use Span for zero-allocation replacements where possible
            return string.Create(text.Length, text, (chars, state) =>
            {
                var textSpan = state.AsSpan();
                int writePos = 0;

                for (int i = 0; i < textSpan.Length; i++)
                {
                    char c = textSpan[i];

                    // Replace Chinese punctuation with English equivalents
                    switch (c)
                    {
                        case '〈': case '〉': case '《': case '》': continue;
                        case '。': c = '.'; break;
                        case '，': c = ','; break;
                        case '；': c = ';'; break;
                        case '：': c = ':'; break;
                        case '？': c = '?'; break;
                        case '！': c = '!'; break;
                        case '（': c = '('; break;
                        case '）': c = ')'; break;
                        case '【': c = '['; break;
                        case '】': c = ']'; break;
                        default: break;
                    }

                    chars[writePos++] = c;
                }

                // Trim trailing spaces
                while (writePos > 0 && char.IsWhiteSpace(chars[writePos - 1]))
                    writePos--;
            });
        }

        public static string DecodeHtmlEntities(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            return HtmlEntity.DeEntitize(text);
        }

        public static bool IsPrimarilyEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            int englishChars = 0;
            int totalChars = 0;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c)) continue;

                totalChars++;

                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                    c == '/' || c == '[' || c == ']' || c == '(' || c == ')' || c == '-' || c == '\'')
                {
                    englishChars++;
                }
            }

            if (totalChars == 0) return false;
            return (englishChars * 100 / totalChars) > 70;
        }

        public static bool ContainsIpaCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return IpaCharRegex.IsMatch(text);
        }

        public static string InferDefinitionFromExample(string example)
        {
            if (string.IsNullOrWhiteSpace(example)) return string.Empty;

            // Check for common definition patterns
            var meansMatch = MeansRegex.Match(example);
            if (meansMatch.Success && meansMatch.Groups.Count > 2)
                return $"{meansMatch.Groups[1].Value} means {meansMatch.Groups[2].Value}";

            var isMatch = IsRegex.Match(example);
            if (isMatch.Success && isMatch.Groups.Count > 2)
                return $"{isMatch.Groups[1].Value} is {isMatch.Groups[2].Value}";

            var refersToMatch = RefersToRegex.Match(example);
            if (refersToMatch.Success && refersToMatch.Groups.Count > 2)
                return $"{refersToMatch.Groups[1].Value} refers to {refersToMatch.Groups[2].Value}";

            // Fallback for short examples
            if (example.Length > 10 && example.Length < 100)
                return example;

            return string.Empty;
        }
    }
}