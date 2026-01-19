using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;

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

            if (raw == null)
                yield break;

            foreach (var entry in ProcessEnglishChineseEntry(raw))
                yield return entry;
        }

        private IEnumerable<DictionaryEntry> ProcessEnglishChineseEntry(EnglishChineseRawEntry raw)
        {
            var entries = new List<DictionaryEntry>();

            try
            {
                // ✅ FIX: Handle actual ENG_CHN format (no ⬄ separator)
                var definition = ExtractEngChnDefinition(raw.RawLine, raw.Headword);

                if (!string.IsNullOrWhiteSpace(definition))
                {
                    var entry = new DictionaryEntry
                    {
                        Word = raw.Headword,
                        NormalizedWord = SourceDataHelper.NormalizeWordWithSourceContext(raw.Headword, SourceCode),
                        Definition = definition,
                        RawFragment = raw.RawLine,
                        SenseNumber = 1,
                        SourceCode = SourceCode,
                        CreatedUtc = DateTime.UtcNow
                    };

                    entries.Add(entry);

                    // Validate Chinese presence for non-abbreviations
                    if (!IsAbbreviation(raw.Headword) && !ContainsChineseCharacters(definition))
                    {
                        _logger.LogWarning("ENG_CHN entry '{Word}' may have lost Chinese characters",
                            raw.Headword);
                    }
                }

                SourceDataHelper.LogProgress(_logger, SourceCode, SourceDataHelper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                SourceDataHelper.HandleError(_logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }

        private string ExtractEngChnDefinition(string rawLine, string headword)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return string.Empty;

            // ✅ FIX: Original data already has the correct format
            // Just clean it up a bit
            var definition = rawLine.Trim();

            // Remove any stray separators if they exist
            var idx = definition.IndexOf('⬄');
            if (idx >= 0 && idx < definition.Length - 1)
            {
                definition = definition[(idx + 1)..].Trim();
            }

            // Clean up extra whitespace
            definition = Regex.Replace(definition, @"\s+", " ").Trim();

            return definition;
        }

        private bool IsAbbreviation(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return false;

            // Check for common abbreviation patterns
            return word.All(c => char.IsUpper(c) || char.IsDigit(c))
                || word.Length <= 3
                || word.Contains(".")
                || Regex.IsMatch(word, @"^[A-Z0-9]+$");
        }

        private bool ContainsChineseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return Regex.IsMatch(text, @"[\u4E00-\u9FFF\u3400-\u4DBF\u3000-\u303F\uff00-\uffef]");
        }
    }
}