using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.FragmentStore;

namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseTransformer(ILogger<EnglishChineseTransformer> logger) : IDataTransformer<EnglishChineseRawEntry>
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
            PartOfSpeech = null,
            Definition = raw.RawLine,
            RawFragment = RawFragments.Save(SourceCode, raw.RawLine, Encoding.UTF8, raw.Headword),
            SenseNumber = 1,
            SourceCode = SourceCode,
            CreatedUtc = DateTime.UtcNow
        };

        logger.LogDebug("ENG_CHN transformed: {Word}", raw.Headword);
    }
}