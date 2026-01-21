using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace DictionaryImporter.Sources.Common.Helper
{
    internal static class Century21HtmlTextHelper
    {
        public static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Normalize whitespace
            text = Regex.Replace(text, @"\s+", " ").Trim();

            // Decode HTML entities safely
            text = HtmlEntity.DeEntitize(text);

            return text.Trim();
        }
    }
}