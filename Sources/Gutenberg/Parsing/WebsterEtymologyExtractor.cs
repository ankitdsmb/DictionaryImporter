using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parshing
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

            return match.Success
                ? match.Groups["text"].Value.Trim()
                : null;
        }
    }
}