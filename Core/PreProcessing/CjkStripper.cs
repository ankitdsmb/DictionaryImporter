namespace DictionaryImporter.Core.PreProcessing;

public static class CjkStripper
{
    private static readonly Regex CjkRegex =
        new(@"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF]",
            RegexOptions.Compiled);

    public static string RemoveCjk(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        return CjkRegex.Replace(input, string.Empty).Trim();
    }
}