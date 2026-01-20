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

        private bool IsAbbreviation(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return false;
            return word.All(c => char.IsUpper(c) || char.IsDigit(c)) ||
                   word.Length <= 3 ||
                   word.Contains(".") ||
                   System.Text.RegularExpressions.Regex.IsMatch(word, @"^[A-Z0-9]+$");
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