using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DictionaryImporter.Sources.Gutenberg;

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
        bool inEntry = false;
        int consecutiveEmptyLines = 0;
        const int MaxConsecutiveEmptyLines = 2;

        await foreach (var line in lines.WithCancellation(cancellationToken))
        {
            var trimmedLine = line.Trim();

            // Skip Project Gutenberg metadata lines
            if (trimmedLine.StartsWith("***") ||
                trimmedLine.StartsWith("Project Gutenberg") ||
                trimmedLine.StartsWith("Title:") ||
                trimmedLine.StartsWith("Author:"))
            {
                continue;
            }

            // Check for end of entry (multiple blank lines or new headword)
            if (inEntry && current != null)
            {
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    consecutiveEmptyLines++;
                    if (consecutiveEmptyLines >= MaxConsecutiveEmptyLines)
                    {
                        // End of current entry
                        if (ValidateGutenbergEntry(current))
                        {
                            if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                                yield break;

                            ParsingHelperGutenberg.UpdateProgress(ref context);
                            yield return current;
                        }
                        current = null;
                        inEntry = false;
                        consecutiveEmptyLines = 0;
                        continue;
                    }
                }
                else
                {
                    consecutiveEmptyLines = 0;

                    // Check if this is a new headword starting
                    if (ParsingHelperGutenberg.IsGutenbergHeadwordLine(trimmedLine, 80))
                    {
                        // Yield previous entry
                        if (ValidateGutenbergEntry(current))
                        {
                            if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                                yield break;

                            ParsingHelperGutenberg.UpdateProgress(ref context);
                            yield return current;
                        }

                        // Start new entry
                        current = new GutenbergRawEntry
                        {
                            Headword = trimmedLine,
                            Lines = new List<string> { trimmedLine }
                        };
                        inEntry = true;
                        continue;
                    }
                }
            }

            // Check for new headword
            if (ParsingHelperGutenberg.IsGutenbergHeadwordLine(trimmedLine, 80))
            {
                if (current != null && ValidateGutenbergEntry(current))
                {
                    if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                        yield break;

                    ParsingHelperGutenberg.UpdateProgress(ref context);
                    yield return current;
                }

                current = new GutenbergRawEntry
                {
                    Headword = trimmedLine,
                    Lines = new List<string> { trimmedLine }
                };
                inEntry = true;
                consecutiveEmptyLines = 0;
                continue;
            }

            // Add line to current entry
            if (current != null && inEntry)
            {
                current.Lines.Add(line);
            }
        }

        // Yield the last entry
        if (current != null && ValidateGutenbergEntry(current))
        {
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
               entry.Lines != null &&
               entry.Lines.Count > 0 &&
               !IsProbablyGutenbergMetadata(entry.Headword);
    }

    private static bool IsProbablyGutenbergMetadata(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        var trimmed = line.Trim();

        // Skip Gutenberg metadata
        if (trimmed.StartsWith("***") ||
            trimmed.StartsWith("Produced by") ||
            trimmed.StartsWith("Transcriber's Note") ||
            trimmed.Contains("Project Gutenberg") ||
            trimmed.StartsWith("Title:") ||
            trimmed.StartsWith("Author:") ||
            trimmed.StartsWith("Release Date:"))
        {
            return true;
        }

        // Skip very long lines that are probably not headwords
        if (trimmed.Length > 120)
            return true;

        return false;
    }
}