using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    /// <summary>
    /// Extracts authoritative Part-of-Speech from Webster
    /// headword headers such as:
    ///   ABASE, v.t.
    ///   ABASED, a.
    /// </summary>
    public static class WebsterHeaderPosExtractor
    {
        private static readonly Regex HeaderPosRegex =
            new(
                @",\s*(?<pos>v\.t\.|v\.i\.|v\.|a\.|n\.)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static (string? Pos, int? Confidence) Extract(
            string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return (null, null);

            var match =
                HeaderPosRegex.Match(definition);

            if (!match.Success)
                return (null, null);

            var token =
                match.Groups["pos"].Value
                    .ToLowerInvariant();

            return token switch
            {
                "v." or "v.t." or "v.i." => ("verb", 100),
                "a." => ("adj", 100),
                "n." => ("noun", 100),
                _ => (null, null)
            };
        }
    }
}
