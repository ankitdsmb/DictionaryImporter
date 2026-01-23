using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.EnglishChinese
{
    public sealed class EnglishChineseTransformer : IDataTransformer<EnglishChineseRawEntry>
    {
        private const string SourceCode = "ENG_CHN";
        private readonly ILogger<EnglishChineseTransformer> _logger;

        public EnglishChineseTransformer(ILogger<EnglishChineseTransformer> logger)
        {
            _logger = logger;
        }

        public IEnumerable<DictionaryEntry> Transform(EnglishChineseRawEntry? raw)
        {
            if (!Helper.ShouldContinueProcessing(SourceCode, _logger))
                yield break;

            if (raw == null)
                yield break;

            var normalizedWord = Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

            yield return new DictionaryEntry
            {
                Word = raw.Headword,
                NormalizedWord = normalizedWord,
                PartOfSpeech = null, // Let parser extract
                Definition = raw.RawLine, // Full line for parsing
                RawFragment = raw.RawLine, // CRITICAL for parser
                SenseNumber = 1,
                SourceCode = SourceCode,
                CreatedUtc = DateTime.UtcNow
            };

            _logger.LogDebug("ENG_CHN transformed: {Word}", raw.Headword);
        }
    }
}