using DictionaryImporter.Common;

namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseTransformer(ILogger<EnglishChineseTransformer> logger)
    : IDataTransformer<EnglishChineseRawEntry>
{
    private const string SourceCode = "ENG_CHN";

    public IEnumerable<DictionaryEntry> Transform(EnglishChineseRawEntry? raw)
    {
        if (!Helper.ShouldContinueProcessing(SourceCode, logger))
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

        logger.LogDebug("ENG_CHN transformed: {Word}", raw.Headword);
    }
}