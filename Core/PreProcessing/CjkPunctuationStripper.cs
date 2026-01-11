using System.Text.RegularExpressions;

namespace DictionaryImporter.Core.PreProcessing
{
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
}
