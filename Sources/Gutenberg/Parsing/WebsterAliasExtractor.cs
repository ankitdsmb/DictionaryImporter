using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parshing
{
    public static class WebsterAliasExtractor
    {
        private static readonly Regex AliasRegex =
            new(
                @"^\s*or\s+(?<alias>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string? ExtractAlias(string? parenthetical)
        {
            if (string.IsNullOrWhiteSpace(parenthetical))
                return null;

            var match = AliasRegex.Match(parenthetical.Trim());
            if (!match.Success)
                return null;

            return match.Groups["alias"].Value.Trim();
        }
    }
}
