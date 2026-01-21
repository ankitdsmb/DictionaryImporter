using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergWebsterExtractor(ILogger<GutenbergWebsterExtractor> logger)
        : IDataExtractor<GutenbergRawEntry>
    {
        private const string SourceCode = "GUT_WEBSTER";

        public async IAsyncEnumerable<GutenbergRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ExtractionHelper.LogExtractionStart(logger, SourceCode);

            var context = ExtractionHelper.CreateExtractorContext(logger, SourceCode);
            var lines = ExtractionHelper.ProcessGutenbergStreamAsync(stream, logger, cancellationToken);

            GutenbergRawEntry? current = null;

            await foreach (var line in lines.WithCancellation(cancellationToken))
            {
                if (TextProcessingHelper.IsHeadword(line, maxLength: 40, requireUppercase: true))
                {
                    // Yield previous entry if exists
                    if (current != null && ValidateGutenbergEntry(current))
                    {
                        // ✅ STRICT: stop before yielding
                        if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, logger))
                            yield break;

                        ExtractionHelper.UpdateProgress(ref context);
                        yield return current;
                    }

                    // Create new entry
                    current = new GutenbergRawEntry
                    {
                        Headword = line.Trim()
                    };
                    current.Lines.Clear();

                    continue;
                }

                if (current != null)
                    current.Lines.Add(line);
            }

            // Yield the last entry
            if (current != null && ValidateGutenbergEntry(current))
            {
                // ✅ STRICT: stop before yielding
                if (!SourceDataHelper.ShouldContinueProcessing(SourceCode, logger))
                    yield break;

                ExtractionHelper.UpdateProgress(ref context);
                yield return current;
            }

            ExtractionHelper.LogExtractionComplete(logger, SourceCode, context.EntryCount);
        }

        private bool ValidateGutenbergEntry(GutenbergRawEntry entry)
        {
            return !string.IsNullOrWhiteSpace(entry.Headword) &&
                   entry.Lines.Count > 0;
        }
    }
}