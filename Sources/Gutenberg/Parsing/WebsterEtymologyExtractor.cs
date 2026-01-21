using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterEtymologyExtractor
    {
        private static readonly Regex EtymRegex =
            new(
                @"Etym:\s*(?<text>.+?)(?=(\n\n|$))",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        public static string? Extract(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            var match = EtymRegex.Match(definition);
            if (!match.Success)
                return null;

            var text = match.Groups["text"].Value.Trim();

            // SAFE: prevent extremely large values
            if (text.Length > 2000)
                text = text[..2000];

            return text;
        }
    }
}