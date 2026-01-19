using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using DictionaryImporter.Sources.Common.Helper;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.EnglishChinese
{
    public sealed class EnglishChineseExtractor(ILogger<EnglishChineseExtractor> logger)
        : IDataExtractor<EnglishChineseRawEntry>
    {
        private const string SourceCode = "ENG_CHN";

        public async IAsyncEnumerable<EnglishChineseRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ExtractionHelper.LogExtractionStart(logger, SourceCode);

            var context = ExtractionHelper.CreateExtractorContext(logger, SourceCode);

            var lines = ExtractionHelper.ProcessStreamWithProgressAsync(
                stream, logger, SourceCode, cancellationToken);

            await foreach (var line in lines.WithCancellation(cancellationToken))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                var headword = TextProcessingHelper.ExtractHeadwordFromSeparator(
                    trimmedLine,
                    '⬄',
                    maxLength: 200);

                if (headword == null)
                    continue;

                var entry = new EnglishChineseRawEntry
                {
                    Headword = headword,
                    RawLine = trimmedLine
                };

                if (!ValidateEnglishChineseEntry(entry))
                    continue;

                // ✅ STRICT: limit applies only to actually-yielded records
                if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, logger))
                    yield break;

                ExtractionHelper.UpdateProgress(ref context);
                yield return entry;
            }

            ExtractionHelper.LogExtractionComplete(logger, SourceCode, context.EntryCount);
        }

        private static bool ValidateEnglishChineseEntry(EnglishChineseRawEntry entry)
        {
            return !string.IsNullOrWhiteSpace(entry.Headword) &&
                   !string.IsNullOrWhiteSpace(entry.RawLine) &&
                   TextProcessingHelper.ContainsEnglishLetters(entry.Headword);
        }
    }
}