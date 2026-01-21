// File: Core/Text/ILanguageDetectionService.cs
namespace DictionaryImporter.Core.Text
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

    public sealed class LanguageDetectionService : ILanguageDetectionService
    {
        // Compiled regex for performance
        private static readonly Regex ChineseCharRegex = new(@"[\u4E00-\u9FFF\u3400-\u4DBF]", RegexOptions.Compiled);
        private static readonly Regex JapaneseCharRegex = new(@"[\u3040-\u309F\u30A0-\u30FF\u31F0-\u31FF]", RegexOptions.Compiled);
        private static readonly Regex KoreanCharRegex = new(@"[\uAC00-\uD7AF\u1100-\u11FF\u3130-\u318F]", RegexOptions.Compiled);
        private static readonly Regex IpaCharRegex = new(@"[\/\[\]ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃ˑ˘˞̴̵̶̷̸̡̢̧̨̘̙̜̝̞̟̠̣̤̥̦̩̪̫̬̭̮̯̰̱̲̳̀́̂̄̆̊̋̌̏̑̚]", RegexOptions.Compiled);
        private static readonly Regex LatinCharRegex = new(@"[A-Za-zÀ-ÿ]", RegexOptions.Compiled);

        public bool ContainsNonEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Check for IPA characters (NOT non-English for dictionaries)
            if (ContainsIpa(text))
                return false;

            // Check for non-Latin scripts
            return ContainsChinese(text) ||
                   ContainsJapanese(text) ||
                   ContainsKorean(text) ||
                   ContainsOtherNonLatinScripts(text);
        }

        public bool IsPrimarilyEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            int latinChars = 0;
            int totalChars = 0;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsDigit(c))
                    continue;

                totalChars++;

                if (LatinCharRegex.IsMatch(c.ToString()))
                {
                    latinChars++;
                }
            }

            if (totalChars == 0)
                return false;

            return (latinChars * 100 / totalChars) > 70;
        }

        // ✅ CRITICAL: Bilingual text detection (e.g., ENG_CHN entries)
        public bool IsBilingualText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool hasLatin = false;
            bool hasNonLatin = false;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                    continue;

                if (LatinCharRegex.IsMatch(c.ToString()))
                {
                    hasLatin = true;
                }
                else if (ContainsChineseChar(c) || ContainsJapaneseChar(c) || ContainsKoreanChar(c))
                {
                    hasNonLatin = true;
                }

                // If we found both Latin and non-Latin characters, it's bilingual
                if (hasLatin && hasNonLatin)
                    return true;
            }

            return false;
        }

        public bool ContainsChinese(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && ChineseCharRegex.IsMatch(text);
        }

        public bool ContainsJapanese(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && JapaneseCharRegex.IsMatch(text);
        }

        public bool ContainsKorean(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && KoreanCharRegex.IsMatch(text);
        }

        public bool ContainsIpa(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && IpaCharRegex.IsMatch(text);
        }

        public string DetectPrimaryLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "unknown";

            // Check for IPA first (special case for dictionaries)
            if (ContainsIpa(text))
                return "ipa";

            if (IsBilingualText(text))
                return "bilingual";

            if (ContainsChinese(text))
                return "zh";

            if (ContainsJapanese(text))
                return "ja";

            if (ContainsKorean(text))
                return "ko";

            if (IsPrimarilyEnglish(text))
                return "en";

            return "unknown";
        }

        public Dictionary<string, double> DetectLanguageDistribution(string text)
        {
            var distribution = new Dictionary<string, double>
            {
                ["latin"] = 0,
                ["chinese"] = 0,
                ["japanese"] = 0,
                ["korean"] = 0,
                ["ipa"] = 0,
                ["other"] = 0
            };

            if (string.IsNullOrWhiteSpace(text))
                return distribution;

            int totalChars = 0;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
                    continue;

                totalChars++;

                if (IsIpaChar(c))
                {
                    distribution["ipa"]++;
                }
                else if (LatinCharRegex.IsMatch(c.ToString()))
                {
                    distribution["latin"]++;
                }
                else if (ContainsChineseChar(c))
                {
                    distribution["chinese"]++;
                }
                else if (ContainsJapaneseChar(c))
                {
                    distribution["japanese"]++;
                }
                else if (ContainsKoreanChar(c))
                {
                    distribution["korean"]++;
                }
                else
                {
                    distribution["other"]++;
                }
            }

            // Convert to percentages
            if (totalChars > 0)
            {
                foreach (var key in distribution.Keys.ToList())
                {
                    distribution[key] = (distribution[key] / totalChars) * 100;
                }
            }

            return distribution;
        }

        // REPLACE the existing ContainsChineseChar, ContainsJapaneseChar, ContainsKoreanChar methods with:
        private bool ContainsChineseChar(char c)
        {
            int code = (int)c;
            return (code >= 0x4E00 && code <= 0x9FFF) ||
                   (code >= 0x3400 && code <= 0x4DBF);
        }

        private bool ContainsJapaneseChar(char c)
        {
            int code = (int)c;
            return (code >= 0x3040 && code <= 0x309F) ||
                   (code >= 0x30A0 && code <= 0x30FF) ||
                   (code >= 0x31F0 && code <= 0x31FF);
        }

        private bool ContainsKoreanChar(char c)
        {
            int code = (int)c;
            return (code >= 0xAC00 && code <= 0xD7AF) ||
                   (code >= 0x1100 && code <= 0x11FF) ||
                   (code >= 0x3130 && code <= 0x318F);
        }
        private bool IsIpaChar(char c)
        {
            return IpaCharRegex.IsMatch(c.ToString());
        }
        // Add these missing methods to LanguageDetectionService class:
        private bool IsChineseChar(char c)
        {
            int code = (int)c;
            return (code >= 0x4E00 && code <= 0x9FFF) ||
                   (code >= 0x3400 && code <= 0x4DBF);
        }

        private bool IsJapaneseChar(char c)
        {
            int code = (int)c;
            return (code >= 0x3040 && code <= 0x309F) ||
                   (code >= 0x30A0 && code <= 0x30FF) ||
                   (code >= 0x31F0 && code <= 0x31FF);
        }

        private bool IsKoreanChar(char c)
        {
            int code = (int)c;
            return (code >= 0xAC00 && code <= 0xD7AF) ||
                   (code >= 0x1100 && code <= 0x11FF) ||
                   (code >= 0x3130 && code <= 0x318F);
        }
        private bool ContainsOtherNonLatinScripts(string text)
        {
            foreach (char c in text)
            {
                if (char.IsLetter(c) && !LatinCharRegex.IsMatch(c.ToString()))
                    return true;
            }
            return false;
        }
    }
}