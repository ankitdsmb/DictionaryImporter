using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using DictionaryImporter.Sources.Common.Helper;
using Microsoft.Extensions.Logging;

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
            ParsingHelperGutenberg.LogExtractionStart(logger, SourceCode);

            var context = ParsingHelperGutenberg.CreateExtractorContext(logger, SourceCode);
            var lines = ParsingHelperGutenberg.ProcessGutenbergStreamAsync(stream, logger, cancellationToken);

            GutenbergRawEntry? current = null;

            await foreach (var line in lines.WithCancellation(cancellationToken))
            {
                // ✅ FIX: use Gutenberg helper logic (NOT SourceDataHelper)
                if (ParsingHelperGutenberg.IsGutenbergHeadwordLine(line, maxLength: 80))
                {
                    // Yield previous entry if exists
                    if (current != null && ValidateGutenbergEntry(current))
                    {
                        // ✅ STRICT: stop before yielding
                        if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                            yield break;

                        ParsingHelperGutenberg.UpdateProgress(ref context);
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
                if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                    yield break;

                ParsingHelperGutenberg.UpdateProgress(ref context);
                yield return current;
            }

            ParsingHelperGutenberg.LogExtractionComplete(logger, SourceCode, context.EntryCount);
        }

        private static bool ValidateGutenbergEntry(GutenbergRawEntry entry)
        {
            return !string.IsNullOrWhiteSpace(entry.Headword) &&
                   entry.Lines.Count > 0;
        }
    }
}
