using System.Text.RegularExpressions;

namespace DictionaryImporter.Sources.Gutenberg.Parsing
{
    internal static class WebsterMeaningGuard
    {
        private static readonly Regex InvalidTitleRegex =
            new(
                @"^(\[[^\]]+\]|\([^)]+\)|Etym:|Note:)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex GarbageTitleRegex =
            new(
                @"^[^A-Za-z]+$",
                RegexOptions.Compiled);

        public static bool IsValidMeaningTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            title = title.Trim();

            if (InvalidTitleRegex.IsMatch(title))
                return false;

            if (GarbageTitleRegex.IsMatch(title))
                return false;

            if (!char.IsLetter(title[0]))
                return false;

            return true;
        }
    }
}