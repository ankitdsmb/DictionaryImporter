using System.Linq;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    public static class WebsterSynonymExtractor
    {
        private static readonly Regex SynonymRegex =
            new(
                @"Syn\.\s*--\s*(?<list>[^.]+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IReadOnlyList<string> Extract(string? definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return [];

            var match = SynonymRegex.Match(definition);
            if (!match.Success)
                return [];

            return match.Groups["list"].Value
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}