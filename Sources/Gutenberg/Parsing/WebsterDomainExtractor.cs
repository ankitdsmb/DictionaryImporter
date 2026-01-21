using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterDomainExtractor
    {
        private static readonly Regex DomainRegex =
            new(@"^\(\s*(?<domain>[A-Za-z]{2,10}\.?)\s*\)", RegexOptions.Compiled);

        public static string? Extract(ref string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            var trimmed = definition.Trim();

            var match = DomainRegex.Match(trimmed);
            if (!match.Success)
                return null;

            var domain = match.Groups["domain"].Value.TrimEnd('.');

            trimmed = trimmed.Substring(match.Length).TrimStart();
            definition = trimmed;

            return domain;
        }
    }
}