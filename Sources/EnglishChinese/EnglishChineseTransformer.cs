using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.EnglishChinese.Parsing;

namespace DictionaryImporter.Sources.EnglishChinese
{
    public sealed class EnglishChineseTransformer : IDataTransformer<EnglishChineseRawEntry>
    {
        private readonly ILogger<EnglishChineseTransformer> _logger;
        private const string SourceCode = "ENG_CHN";

        public EnglishChineseTransformer(ILogger<EnglishChineseTransformer> logger)
        {
            _logger = logger;
        }

        public IEnumerable<DictionaryEntry> Transform(EnglishChineseRawEntry raw)
        {
            if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, _logger))
                yield break;

            if (raw == null) yield break;

            foreach (var entry in ProcessEnglishChineseEntry(raw))
                yield return entry;
        }

        private IEnumerable<DictionaryEntry> ProcessEnglishChineseEntry(EnglishChineseRawEntry raw)
        {
            var entries = new List<DictionaryEntry>();

            try
            {
                // ✅ REPLACE the broken extraction with SimpleEngChnExtractor
                var definition = SimpleEngChnExtractor.ExtractDefinition(raw.RawLine);

                if (!string.IsNullOrWhiteSpace(definition))
                {
                    var entry = new DictionaryEntry
                    {
                        Word = raw.Headword,
                        NormalizedWord = SourceDataHelper.NormalizeWordWithSourceContext(
                            raw.Headword, SourceCode),
                        Definition = definition,
                        RawFragment = raw.RawLine, // Keep original for reference
                        SenseNumber = 1,
                        SourceCode = SourceCode,
                        CreatedUtc = DateTime.UtcNow
                    };

                    entries.Add(entry);

                    // Log if Chinese characters might have been lost
                    if (!IsAbbreviation(raw.Headword) &&
                        !ContainsChineseCharacters(definition))
                    {
                        _logger.LogWarning(
                            "ENG_CHN entry '{Word}' may have lost Chinese characters. Raw: {RawPreview}",
                            raw.Headword,
                            GetPreview(raw.RawLine, 100));
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "ENG_CHN entry '{Word}' produced empty definition. Raw: {RawPreview}",
                        raw.Headword,
                        GetPreview(raw.RawLine, 50));
                }

                SourceDataHelper.LogProgress(_logger, SourceCode,
                    SourceDataHelper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                SourceDataHelper.HandleError(_logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }

        private static bool IsAbbreviation(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            // Clean the word but preserve structure for analysis
            var cleanWord = word.Replace(" ", ""); // Remove spaces but keep commas

            // Check for TRUE abbreviation patterns:

            // 1. Contains digits AND letters with comma pattern (like "2,4-D", "3,4-D")
            if (Regex.IsMatch(cleanWord, @"^\d+(?:,\d+)+[A-Za-z]?$"))
                return true;

            // 2. Contains digits AND letters (like "2,4-D", "3D", "A1")
            if (Regex.IsMatch(cleanWord, @"\d+[A-Za-z]|[A-Za-z]\d+"))
                return true;

            // 3. ALL UPPERCASE (or mostly uppercase) with possible digits, commas and hyphens
            var lettersOnly = Regex.Replace(cleanWord, @"[^A-Za-z]", "");
            if (!string.IsNullOrEmpty(lettersOnly))
            {
                var uppercaseRatio = lettersOnly.Count(char.IsUpper) / (double)Math.Max(1, lettersOnly.Length);
                if (uppercaseRatio > 0.7 && cleanWord.Length <= 8)
                    return true;
            }

            // 4. Contains dots between letters (like "U.S.A.", "a.m.", "p.m.")
            if (word.Contains('.') && Regex.IsMatch(word, @"[A-Za-z]\.[A-Za-z]"))
                return true;

            // 5. Very short (1-3 chars) and looks like initials
            if (word.Length <= 3)
            {
                var letterCount = word.Count(char.IsLetter);
                if (letterCount > 0)
                {
                    var uppercaseCount = word.Count(char.IsUpper);
                    if (uppercaseCount >= letterCount)
                        return true;
                }
            }

            // 6. Specific chemical/technical abbreviations with comma pattern
            if (Regex.IsMatch(word, @"^\d+(?:,\s*\d+)*\-[A-Za-z]$"))
                return true;

            return false;
        }

        private bool ContainsChineseCharacters(string text)
        {
            return SimpleEngChnExtractor.ContainsChinese(text);
        }

        private string GetPreview(string text, int length)
        {
            if (string.IsNullOrWhiteSpace(text)) return "[empty]";
            if (text.Length <= length) return text;
            return text.Substring(0, length) + "...";
        }
    }
}