using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Provides shared extraction utilities for dictionary sources.
    /// </summary>
    public static class ExtractionHelper
    {
        #region Progress Tracking and Logging

        /// <summary>
        /// Logs extraction start.
        /// </summary>
        public static void LogExtractionStart(ILogger logger, string sourceName)
        {
            logger.LogInformation("{Source} extraction started", sourceName);
        }

        /// <summary>
        /// Logs extraction completion.
        /// </summary>
        public static void LogExtractionComplete(ILogger logger, string sourceName, long entryCount)
        {
            logger.LogInformation("{Source} extraction completed. Entries: {Count}",
                sourceName, entryCount);
        }

        /// <summary>
        /// Logs progress at intervals.
        /// </summary>
        public static void LogExtractionProgress(ILogger logger, string sourceName, long entryCount, int interval = 1000)
        {
            if (entryCount % interval == 0)
            {
                logger.LogInformation("{Source} extraction progress: {Count} entries processed",
                    sourceName, entryCount);
            }
        }

        /// <summary>
        /// Creates a standard extractor execution context.
        /// </summary>
        public static ExtractorContext CreateExtractorContext(ILogger logger, string sourceName)
        {
            return new ExtractorContext
            {
                Logger = logger,
                SourceName = sourceName,
                EntryCount = 0
            };
        }

        /// <summary>
        /// Updates and logs progress.
        /// </summary>
        public static void UpdateProgress(ref ExtractorContext context)
        {
            context.EntryCount++;
            LogExtractionProgress(context.Logger, context.SourceName, context.EntryCount);
        }

        #endregion Progress Tracking and Logging

        #region Stream Processing

        /// <summary>
        /// Processes a stream line by line with Gutenberg-style start/end markers.
        /// </summary>
        public static async IAsyncEnumerable<string> ProcessGutenbergStreamAsync(
            Stream stream,
            ILogger logger,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 16 * 1024, true);

            string? line;
            var bodyStarted = false;
            long lineCount = 0;
            long bodyLineCount = 0;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineCount++;

                if (!bodyStarted)
                {
                    if (line.StartsWith("*** START"))
                    {
                        bodyStarted = true;
                        logger.LogInformation("Gutenberg body detected at line {LineNumber}", lineCount);
                    }

                    continue;
                }

                if (line.StartsWith("*** END"))
                {
                    logger.LogInformation("Gutenberg end marker at line {LineNumber}", lineCount);
                    break;
                }

                bodyLineCount++;
                yield return line;
            }

            logger.LogInformation(
                "Gutenberg stream processing completed: {BodyLines} body lines (TotalLinesRead={TotalLines})",
                bodyLineCount,
                lineCount);
        }

        /// <summary>
        /// Processes a stream line by line with progress tracking.
        /// </summary>
        public static async IAsyncEnumerable<string> ProcessStreamWithProgressAsync(
            Stream stream,
            ILogger logger,
            string sourceName,
            [EnumeratorCancellation] CancellationToken cancellationToken,
            int progressInterval = 10000)
        {
            using var reader = new StreamReader(stream);

            long lineCount = 0;
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineCount++;

                if (lineCount % progressInterval == 0)
                {
                    logger.LogInformation("{Source} processing progress: {Count} lines processed",
                        sourceName, lineCount);
                }

                yield return line;
            }

            logger.LogInformation("{Source} processing completed: {Count} total lines",
                sourceName, lineCount);
        }

        #endregion Stream Processing

        #region Helper Classes

        /// <summary>
        /// Context for extractor execution.
        /// </summary>
        public class ExtractorContext
        {
            public ILogger Logger { get; set; } = null!;
            public string SourceName { get; set; } = null!;
            public long EntryCount { get; set; }
        }

        #endregion Helper Classes
    }
}