using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterEtymologyLanguageDetector
    {
        private static readonly (Regex Regex, string Code)[] Rules =
        [
            (new Regex(@"\b(Lat\.|L\.)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "lat"),
            (new Regex(@"\bGr\.\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "grc"),
            (new Regex(@"\bFr\.\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "fr"),
            (new Regex(@"\bAS\.\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ang"),
            (new Regex(@"\bGer\.\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "de"),
            (new Regex(@"\bIt\.\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "it"),
            (new Regex(@"\bSp\.\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "es")
        ];

        public static string? Detect(string etymologyText)
        {
            if (string.IsNullOrWhiteSpace(etymologyText))
                return null;

            foreach (var (regex, code) in Rules)
                if (regex.IsMatch(etymologyText))
                    return code;

            return null;
        }
    }
}