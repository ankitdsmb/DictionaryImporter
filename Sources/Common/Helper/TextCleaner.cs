namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Handles cleaning and processing of text content.
    /// </summary>
    public static class TextCleaner
    {
        private static readonly char[] _quoteChars = { '"', '\'', '`', '«', '»', '「', '」', '『', '』' };
        private static readonly string[] _templateMarkers = { "{{", "}}", "[[", "]]" };

        private static readonly Dictionary<string, string> _languagePatterns = new()
        {
            { @"\bLatin\b", "la" },
            { @"\bAncient Greek\b|\bGreek\b", "el" },
            { @"\bFrench\b", "fr" },
            { @"\bGerman(ic)?\b", "de" },
            { @"\bOld English\b", "ang" },
            { @"\bMiddle English\b", "enm" },
            { @"\bItalian\b", "it" },
            { @"\bSpanish\b", "es" },
            { @"\bDutch\b", "nl" },
            { @"\bProto-Indo-European\b", "ine-pro" },
            { @"\bOld Norse\b", "non" },
            { @"\bOld French\b", "fro" },
            { @"\bAnglo-Norman\b", "xno" }
        };

        /// <summary>
        /// Cleans etymology text by removing template markers and HTML.
        /// </summary>
        public static string CleanEtymologyText(string etymology)
        {
            if (string.IsNullOrWhiteSpace(etymology))
                return string.Empty;

            var cleaned = Regex.Replace(etymology, @"\s+", " ").Trim();

            foreach (var marker in _templateMarkers)
            {
                cleaned = cleaned.Replace(marker, "");
            }

            cleaned = Regex.Replace(cleaned, @"<[^>]+>", "");

            return cleaned.Trim();
        }

        /// <summary>
        /// Cleans example text by removing quotes and translations.
        /// </summary>
        public static string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            var cleaned = example.Trim(_quoteChars);
            cleaned = Regex.Replace(cleaned, @"\s*\([^)]*\)\s*", " ");

            if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
            {
                cleaned += ".";
            }

            return cleaned.Trim();
        }

        /// <summary>
        /// Detects language code from etymology text.
        /// </summary>
        public static string? DetectLanguageFromEtymology(string etymology)
        {
            if (string.IsNullOrWhiteSpace(etymology))
                return null;

            foreach (var pattern in _languagePatterns)
            {
                if (Regex.IsMatch(etymology, pattern.Key, RegexOptions.IgnoreCase))
                {
                    return pattern.Value;
                }
            }

            return null;
        }
    }
}