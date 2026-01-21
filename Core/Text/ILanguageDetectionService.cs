// File: Core/Text/ILanguageDetectionService.cs
namespace DictionaryImporter.Core.Text
{
    public interface ILanguageDetectionService
    {
        bool ContainsNonEnglishText(string text);

        string? DetectLanguageCode(string text);

        bool ContainsChineseText(string text);

        bool IsBilingualText(string text);
    }

    public class LanguageDetectionService : ILanguageDetectionService
    {
        public bool ContainsNonEnglishText(string text) => LanguageDetector.ContainsNonEnglishText(text);

        public string? DetectLanguageCode(string text) => LanguageDetector.DetectLanguageCode(text);

        public bool ContainsChineseText(string text) => LanguageDetector.ContainsChineseText(text);

        public bool IsBilingualText(string text) => LanguageDetector.IsBilingualText(text);
    }
}