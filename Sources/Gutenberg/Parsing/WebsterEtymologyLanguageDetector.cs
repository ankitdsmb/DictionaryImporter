using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterEtymologyLanguageDetector
    {
        private static readonly (Regex Regex, string Code)[] Rules =
        {
            (new Regex(@"\b(Lat\.|L\.)\b", RegexOptions.IgnoreCase), "lat"),
            (new Regex(@"\bGr\.\b", RegexOptions.IgnoreCase), "grc"),
            (new Regex(@"\bFr\.\b", RegexOptions.IgnoreCase), "fr"),
            (new Regex(@"\bAS\.\b", RegexOptions.IgnoreCase), "ang"),
            (new Regex(@"\bGer\.\b", RegexOptions.IgnoreCase), "de"),
            (new Regex(@"\bIt\.\b", RegexOptions.IgnoreCase), "it"),
            (new Regex(@"\bSp\.\b", RegexOptions.IgnoreCase), "es")
        };
        public static string? Detect(string etymologyText)
        {
            if (string.IsNullOrWhiteSpace(etymologyText))
                return null;

            foreach (var (regex, code) in Rules)
            {
                if (regex.IsMatch(etymologyText))
                    return code;
            }

            return null;
        }
    }
}