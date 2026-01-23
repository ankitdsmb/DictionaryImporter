// File: Core/Text/ILanguageDetectionService.cs
namespace DictionaryImporter.Core.Abstractions
{
    public interface ILanguageDetectionService
    {
        // Basic detection
        bool ContainsNonEnglish(string text);
        bool IsPrimarilyEnglish(string text);

        // Bilingual detection (CRITICAL FOR ENG_CHN)
        bool IsBilingualText(string text);
        bool ContainsChinese(string text);
        bool ContainsJapanese(string text);
        bool ContainsKorean(string text);

        // Advanced detection
        string DetectPrimaryLanguage(string text);
        Dictionary<string, double> DetectLanguageDistribution(string text);

        // IPA detection (should NOT be flagged as non-English)
        bool ContainsIpa(string text);
    }
}