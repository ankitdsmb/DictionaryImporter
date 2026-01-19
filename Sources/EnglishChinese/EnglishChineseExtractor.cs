using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Sources.Common.Helper;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.EnglishChinese
{
    public sealed class EnglishChineseExtractor : IDataExtractor<EnglishChineseRawEntry>
    {
        private const string SourceCode = "ENG_CHN";
        private readonly ILogger<EnglishChineseExtractor> _logger;

        public EnglishChineseExtractor(ILogger<EnglishChineseExtractor> logger)
        {
            _logger = logger;
        }

        public async IAsyncEnumerable<EnglishChineseRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ExtractionHelper.LogExtractionStart(_logger, SourceCode);
            var context = ExtractionHelper.CreateExtractorContext(_logger, SourceCode);

            var lines = ExtractionHelper.ProcessStreamWithProgressAsync(
                stream, _logger, SourceCode, cancellationToken);

            await foreach (var line in lines.WithCancellation(cancellationToken))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    continue;

                // ✅ FIX: Extract headword more flexibly
                var headword = ExtractHeadwordFromEngChnLine(trimmedLine);
                if (headword == null)
                    continue;

                var entry = new EnglishChineseRawEntry
                {
                    Headword = headword,
                    RawLine = trimmedLine
                };

                if (!ValidateEnglishChineseEntry(entry))
                    continue;

                if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, _logger))
                    yield break;

                ExtractionHelper.UpdateProgress(ref context);
                yield return entry;
            }

            ExtractionHelper.LogExtractionComplete(_logger, SourceCode, context.EntryCount);
        }

        private string ExtractHeadwordFromEngChnLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            // ✅ FIX: ENG_CHN format is: word [pronunciation] part_of_speech. definition
            // Extract first word (until first space or special character)
            var endIndex = line.IndexOf(' ');
            if (endIndex <= 0)
                endIndex = line.Length;

            var headword = line.Substring(0, endIndex).Trim();

            // Clean up any trailing punctuation
            headword = headword.TrimEnd('.', ',', ';', ':', '!', '?', '·');

            // Validate it contains English letters or numbers
            if (!TextProcessingHelper.ContainsEnglishLetters(headword) &&
                !headword.Any(char.IsDigit))
                return null;

            return headword;
        }

        private static bool ValidateEnglishChineseEntry(EnglishChineseRawEntry entry)
        {
            return !string.IsNullOrWhiteSpace(entry.Headword)
                && !string.IsNullOrWhiteSpace(entry.RawLine)
                && entry.Headword.Length <= 100
                && entry.RawLine.Length <= 8000;
        }
    }
}