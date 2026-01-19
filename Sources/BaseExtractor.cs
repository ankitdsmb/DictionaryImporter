namespace DictionaryImporter.Sources.Common
{
    /// <summary>
    /// Base class for data extractors with common functionality.
    /// </summary>
    /// <typeparam name="TRawEntry">The type of raw entry to extract.</typeparam>
    public abstract class BaseExtractor<TRawEntry> : IDataExtractor<TRawEntry>
        where TRawEntry : class, new()
    {
        protected readonly ILogger Logger;
        protected long EntryCount = 0;

        protected BaseExtractor(ILogger logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Main extraction method to be implemented by derived classes.
        /// </summary>
        public abstract IAsyncEnumerable<TRawEntry> ExtractAsync(
            Stream stream,
            CancellationToken cancellationToken);

        /// <summary>
        /// Logs extraction start.
        /// </summary>
        protected void LogExtractionStart(string sourceName)
        {
            Logger.LogInformation("{Source} extraction started", sourceName);
        }

        /// <summary>
        /// Logs extraction completion.
        /// </summary>
        protected void LogExtractionComplete(string sourceName)
        {
            Logger.LogInformation("{Source} extraction completed. Entries: {Count}",
                sourceName, EntryCount);
        }

        /// <summary>
        /// Logs progress at intervals.
        /// </summary>
        protected void LogProgress(string sourceName)
        {
            if (EntryCount % 1000 == 0)
            {
                Logger.LogInformation("{Source} extraction progress: {Count} entries processed",
                    sourceName, EntryCount);
            }
        }

        /// <summary>
        /// Safely processes a line and creates an entry if valid.
        /// </summary>
        protected virtual TRawEntry? ProcessLine(string line, string sourceName)
        {
            return null;
        }

        /// <summary>
        /// Creates a new entry instance.
        /// </summary>
        protected virtual TRawEntry CreateEntry()
        {
            return new TRawEntry();
        }

        /// <summary>
        /// Validates an entry before yielding.
        /// </summary>
        protected virtual bool ValidateEntry(TRawEntry entry)
        {
            return entry != null;
        }
    }
}