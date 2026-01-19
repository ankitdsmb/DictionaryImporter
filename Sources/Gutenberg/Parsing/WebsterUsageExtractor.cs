using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterUsageExtractor
    {
        private static readonly Regex UsageRegex =
            new(
                @"^\s*(?:\[(?<tag>Obs\.?|R\.?|Archaic\.?)\]|(?<tag>Obs\.?|R\.?|Archaic\.?))\s*",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string? Extract(ref string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            var text = definition.TrimStart();

            var match = UsageRegex.Match(text);
            if (!match.Success)
                return null;

            var raw =
                match.Groups["tag"].Value
                    .Trim()
                    .TrimEnd('.')
                    .ToLowerInvariant();

            var usage = raw switch
            {
                "obs" => "obsolete",
                "r" => "rare",
                "archaic" => "archaic",
                _ => null
            };

            if (usage == null)
                return null;

            text = text.Substring(match.Length).TrimStart();
            definition = text;

            return usage;
        }
    }
}