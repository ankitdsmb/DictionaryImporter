using System.Text.RegularExpressions;

namespace DictionaryImporter.Core.PreProcessing
{
    /// <summary>
    /// Generic IPA extractor that:
    /// 1. Extracts IPA from /slashes/ (preferred)
    /// 2. Falls back to raw IPA ONLY if IPA Unicode exists
    /// 3. Applies STRICT IPA sanitation
    /// 4. Supports multiple IPA variants
    /// 5. Detects locale per IPA
    /// 6. RETURNS IPA IN CANONICAL /.../ FORMAT
    ///
    /// ✔ No grammar rules
    /// ✔ QA-safe
    /// ✔ Idempotent
    /// </summary>
    internal static class GenericIpaExtractor
    {
        // -------------------------------
        // Core patterns
        // -------------------------------
        private static readonly Regex SlashBlockRegex =
            new(@"/([^/]+)/", RegexOptions.Compiled);

        // IPA Unicode presence (must-have)
        private static readonly Regex IpaPresenceRegex =
            new(@"[ˈˌɑ-ʊəɐɛɪɔʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃ː]",
                RegexOptions.Compiled);

        // Allowed IPA characters (whitelist)
        private static readonly Regex IpaAllowedCharsRegex =
            new(@"[^ˈˌɑ-ʊəɐɛɪɔʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃ː\. ]",
                RegexOptions.Compiled);

        // Obvious junk
        private static readonly Regex RejectRegex =
            new(@"^[0-9\s./:-]+$", RegexOptions.Compiled);

        // Editorial / explanatory words
        private static readonly Regex ProseRegex =
            new(@"\b(strong|weak|form|plural|singular)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Punctuation cleanup
        private static readonly Regex EditorialPunctuationRegex =
            new(@"[.,，]", RegexOptions.Compiled);

        // Parentheses IPA (e.g. (r))
        private static readonly Regex ParenthesesRegex =
            new(@"[\(\)]", RegexOptions.Compiled);

        // Leading / trailing hyphens
        private static readonly Regex EdgeHyphenRegex =
            new(@"(^-)|(-$)", RegexOptions.Compiled);

        /// <summary>
        /// Extracts DISTINCT IPA → Locale mappings.
        /// IPA is ALWAYS returned in canonical /.../ format.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ExtractIpaWithLocale(string? text)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(text))
                return result;

            // 1. Prefer slash IPA
            var slashMatches = SlashBlockRegex.Matches(text);

            IEnumerable<string> candidates =
                slashMatches.Count > 0
                    ? slashMatches.Select(m => m.Groups[1].Value)
                    : new[] { text };

            foreach (var raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (RejectRegex.IsMatch(raw))
                    continue;

                if (ProseRegex.IsMatch(raw))
                    continue;

                // Must contain IPA Unicode somewhere
                if (!IpaPresenceRegex.IsMatch(raw))
                    continue;

                // 2. Mechanical cleanup
                var cleaned = raw;

                cleaned = cleaned.Replace(":", "ː");
                cleaned = EditorialPunctuationRegex.Replace(cleaned, "");
                cleaned = ParenthesesRegex.Replace(cleaned, "");
                cleaned = IpaAllowedCharsRegex.Replace(cleaned, "");
                cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

                if (cleaned.Length == 0)
                    continue;

                // 3. Split multiple IPA variants
                var parts =
                    cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    var ipaCore =
                        EdgeHyphenRegex.Replace(part.Trim(), "");

                    if (ipaCore.Length == 0)
                        continue;

                    if (!IpaPresenceRegex.IsMatch(ipaCore))
                        continue;

                    var canonicalIpa = IpaAutoStressNormalizer.Normalize($"/{ipaCore}/");

                    if (result.ContainsKey(canonicalIpa))
                        continue;

                    var detectedLocale =
                        IpaLocaleDetector.Detect(ipaCore);

                    var systemLocale =
                        IpaLocaleDetector.MapToSystemLocale(detectedLocale);

                    if (!string.IsNullOrWhiteSpace(systemLocale))
                    {
                        result.Add(canonicalIpa, systemLocale);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Removes all slash-enclosed IPA blocks from text.
        /// </summary>
        public static string RemoveAll(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return SlashBlockRegex.Replace(text, "").Trim();
        }
    }
}
