using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Common.Helper
{
    /// <summary>
    /// Provides standardized logging methods.
    /// </summary>
    public static class LoggingHelper
    {
        /// <summary>
        /// Logs processing progress at intervals.
        /// </summary>
        public static void LogProgress(ILogger logger, string sourceCode, int count)
        {
            if (count % 10 == 0)
            {
                logger.LogInformation("{Source} processing progress: {Count} records processed",
                    sourceCode, count);
            }
        }

        /// <summary>
        /// Logs when maximum records per source is reached.
        /// </summary>
        public static void LogMaxReached(ILogger logger, string sourceCode, int maxRecords)
        {
            logger.LogInformation("Reached maximum of {MaxRecords} records for {Source} source",
                maxRecords, sourceCode);
        }

        /// <summary>
        /// Handles errors with consistent logging.
        /// </summary>
        public static void HandleError(ILogger logger, Exception ex, string sourceCode, string operation)
        {
            logger.LogError(ex, "Error {Operation} for {Source} entry", operation, sourceCode);
        }

        /// <summary>
        /// Logs warning for non-critical issues.
        /// </summary>
        public static void LogWarning(ILogger logger, string message, params object[] args)
        {
            logger.LogWarning(message, args);
        }

        /// <summary>
        /// Logs debug information for troubleshooting.
        /// </summary>
        public static void LogDebug(ILogger logger, string message, params object[] args)
        {
            logger.LogDebug(message, args);
        }
    }
}