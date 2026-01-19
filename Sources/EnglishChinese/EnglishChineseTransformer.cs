using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.EnglishChinese
{
    public sealed class EnglishChineseTransformer(ILogger<EnglishChineseTransformer> logger)
        : IDataTransformer<EnglishChineseRawEntry>
    {
        private const string SourceCode = "ENG_CHN";

        public IEnumerable<DictionaryEntry> Transform(EnglishChineseRawEntry raw)
        {
            if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, logger))
                yield break;

            // SAFETY: do not crash pipeline if raw is unexpectedly null
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
                var idx = raw.RawLine.IndexOf('⬄');

                if (idx >= 0 && idx < raw.RawLine.Length - 1)
                {
                    var rhs = raw.RawLine[(idx + 1)..].Trim();

                    if (!string.IsNullOrEmpty(rhs))
                    {
                        entries.Add(new DictionaryEntry
                        {
                            Word = raw.Headword,
                            NormalizedWord = SourceDataHelper.NormalizeWord(raw.Headword), // FIX
                            Definition = rhs,
                            RawFragment = raw.RawLine, // FIX
                            SenseNumber = 1,
                            SourceCode = SourceCode,
                            CreatedUtc = DateTime.UtcNow
                        });
                    }
                }

                SourceDataHelper.LogProgress(logger, SourceCode, SourceDataHelper.GetCurrentCount(SourceCode));
            }
            catch (Exception ex)
            {
                // SAFETY: log and continue (no rethrow)
                SourceDataHelper.HandleError(logger, ex, SourceCode, "transforming");
            }

            foreach (var entry in entries)
                yield return entry;
        }
    }
}