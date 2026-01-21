// File: Core/Text/LanguageDetector.cs
namespace DictionaryImporter.Core.Text
{
    public static class LanguageDetector
    {
        private static readonly ILanguageDetectionService _service = new LanguageDetectionService();

        public static bool ContainsNonEnglishText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return _service.ContainsNonEnglish(text);
        }

        public static string? DetectLanguageCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return _service.DetectPrimaryLanguage(text);
        }

        public static bool IsBilingualText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return _service.IsBilingualText(text);
        }
    }
}