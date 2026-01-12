namespace DictionaryImporter.Core.PreProcessing;

public static class CjkPunctuationStripper
{
    private static readonly Regex CjkPunctuationRegex =
        new(@"[，。、；：！？【】（）《》〈〉「」『』]",
            RegexOptions.Compiled);

    public static string RemoveCjkPunctuation(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        return CjkPunctuationRegex.Replace(input, string.Empty).Trim();
    }
}