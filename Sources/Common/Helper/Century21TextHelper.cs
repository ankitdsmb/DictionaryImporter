using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace DictionaryImporter.Sources.Common.Helper
{
    internal static class Century21TextHelper
    {
        public static string CleanEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = DecodeHtmlEntities(text);
            text = RemoveChineseCharacters(text);
            text = RemoveChineseMarkers(text);
            text = Regex.Replace(text, "<.*?>", string.Empty);
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        public static string RemoveChineseCharacters(string text)
        {
            return Regex.Replace(
                text,
                @"[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3000-\u303F\uff00-\uffef]",
                string.Empty);
        }

        public static string RemoveChineseMarkers(string text)
        {
            return text.Replace("〈", "")
                .Replace("〉", "")
                .Replace("《", "")
                .Replace("》", "")
                .Replace("。", ". ")
                .Replace("，", ", ")
                .Replace("；", "; ")
                .Replace("：", ": ")
                .Replace("？", "? ")
                .Replace("！", "! ")
                .Replace("（", "(")
                .Replace("）", ")")
                .Replace("【", "[")
                .Replace("】", "]")
                .Trim();
        }

        public static string DecodeHtmlEntities(string text)
        {
            return HtmlEntity.DeEntitize(text);
        }

        public static bool IsPrimarilyEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var englishChars = 0;
            var totalChars = 0;

            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                    continue;

                totalChars++;

                if (c >= 'A' && c <= 'Z' ||
                    c >= 'a' && c <= 'z' ||
                    c >= '0' && c <= '9' ||
                    c == '/' || c == '[' || c == ']' ||
                    c == '(' || c == ')' || c == '-' || c == '\'')
                {
                    englishChars++;
                }
            }

            if (totalChars == 0)
                return false;

            return englishChars * 100 / totalChars > 70;
        }

        public static bool ContainsIpaCharacters(string text)
        {
            return Regex.IsMatch(text, @"[\/\[\]ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃]");
        }

        public static string InferDefinitionFromExample(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            if (example.Contains(" means "))
            {
                var match = Regex.Match(example, @"(\w+)\s+means\s+(.+)", RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 2)
                    return $"{match.Groups[1].Value} means {match.Groups[2].Value}";
            }

            if (example.Contains(" is "))
            {
                var match = Regex.Match(example, @"(\w+)\s+is\s+(.+)", RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 2)
                    return $"{match.Groups[1].Value} is {match.Groups[2].Value}";
            }

            if (example.Contains(" refers to "))
            {
                var match = Regex.Match(example, @"(\w+)\s+refers to\s+(.+)", RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 2)
                    return $"{match.Groups[1].Value} refers to {match.Groups[2].Value}";
            }

            if (example.Length > 10 && example.Length < 100)
                return example;

            return string.Empty;
        }
    }
}