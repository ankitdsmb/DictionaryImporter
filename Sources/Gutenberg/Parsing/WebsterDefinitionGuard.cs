using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterDefinitionGuard
    {
        private static readonly Regex EtymologyRegex =
            new(@"^\s*Etym:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NoteRegex =
            new(@"^\s*Note:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DomainOnlyRegex =
            new(@"^\s*\([A-Za-z\.]+\)\s*$", RegexOptions.Compiled);

        public static bool IsValid(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return false;

            definition = definition.Trim();

            if (EtymologyRegex.IsMatch(definition))
                return false;

            if (NoteRegex.IsMatch(definition))
                return false;

            if (DomainOnlyRegex.IsMatch(definition))
                return false;

            return true;
        }
    }
}
