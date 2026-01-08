using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parshing
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
                return Array.Empty<string>();

            var match = SynonymRegex.Match(definition);
            if (!match.Success)
                return Array.Empty<string>();

            return match.Groups["list"].Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 1)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}