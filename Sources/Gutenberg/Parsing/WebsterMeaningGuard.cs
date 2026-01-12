namespace DictionaryImporter.Sources.Gutenberg.Parsing;

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

        // Reject labels like [R.], [Obs.]
        if (InvalidTitleRegex.IsMatch(title))
            return false;

        // Reject non-word garbage
        if (GarbageTitleRegex.IsMatch(title))
            return false;

        // Must start with a letter
        if (!char.IsLetter(title[0]))
            return false;

        return true;
    }
}