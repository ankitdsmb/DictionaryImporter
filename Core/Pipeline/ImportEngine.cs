using DictionaryImporter.Common;
using DictionaryImporter.Infrastructure.Validation;

namespace DictionaryImporter.Core.Pipeline
{
    public sealed class ImportEngine<TRaw>(
        IDataExtractor<TRaw> extractor,
        IDataTransformer<TRaw> transformer,
        IDataLoader loader,
        IDictionaryEntryValidator validator,
        ILogger<ImportEngine<TRaw>> logger)
        : IImportEngine
    {
        private const int BatchSize = Helper.MAX_RECORDS_PER_SOURCE;
        private int _totalProcessed = 0;
        private int _totalValid = 0;
        private int _totalInvalid = 0;

        async Task IImportEngine.ImportAsync(
            Stream source,
            CancellationToken ct)
        {
            await ImportAsync(source, ct);
        }

        public async Task ImportAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var entries = new List<DictionaryEntry>();

            try
            {
                logger.LogInformation("Starting import process");

                // ✅ FIX: Track total entries properly
                _totalProcessed = 0;
                _totalValid = 0;
                _totalInvalid = 0;

                // Extract raw entries
                await foreach (var rawEntry in extractor.ExtractAsync(stream, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // ✅ FIX: Log raw entry extraction
                    logger.LogDebug("Extracted raw entry: {RawType}", rawEntry?.GetType().Name);

                    // Transform each raw entry to DictionaryEntry
                    var transformedEntries = transformer.Transform(rawEntry);
                    var transformedList = transformedEntries?.ToList() ?? new List<DictionaryEntry>();

                    // ✅ FIX: Handle null/empty transformation
                    if (transformedList.Count == 0)
                    {
                        logger.LogDebug("Transformer returned empty result for raw entry");
                        continue;
                    }

                    foreach (var entry in transformedList)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _totalProcessed++;

                        // ✅ FIX: Apply preprocessing BEFORE validation
                        var processedEntry = Preprocess(entry);

                        // VALIDATE each entry
                        var validationResult = validator.Validate(processedEntry);

                        if (validationResult.IsValid)
                        {
                            entries.Add(processedEntry);
                            _totalValid++;

                            if (_totalValid % 100 == 0)
                            {
                                logger.LogInformation("Import progress: {Valid} valid entries", _totalValid);
                            }
                        }
                        else
                        {
                            _totalInvalid++;
                            var errorMessage = GetValidationErrorMessage(validationResult);
                            logger.LogDebug("Skipped invalid entry: {Word} - {Reason}",
                                processedEntry.Word, errorMessage);
                        }

                        // Check batch size and load if needed
                        if (entries.Count >= BatchSize)
                        {
                            await FlushBatchAsync(entries, cancellationToken);
                        }
                    }
                }

                // ✅ FIX: Load any remaining entries
                if (entries.Count > 0)
                {
                    await FlushBatchAsync(entries, cancellationToken);
                }

                logger.LogInformation(
                    "Import completed successfully. " +
                    "Total processed: {Processed}, " +
                    "Valid: {Valid}, " +
                    "Invalid: {Invalid}, " +
                    "Loaded to staging: {Loaded}",
                    _totalProcessed,
                    _totalValid,
                    _totalInvalid,
                    _totalValid); // Assuming all valid entries were loaded
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Import failed. Stats: Processed={Processed}, Valid={Valid}, Invalid={Invalid}",
                    _totalProcessed, _totalValid, _totalInvalid);
                throw;
            }
        }

        private async Task FlushBatchAsync(
            List<DictionaryEntry> batch,
            CancellationToken ct)
        {
            if (batch == null || batch.Count == 0)
                return;

            try
            {
                logger.LogDebug("Flushing batch of {Count} entries", batch.Count);
                await loader.LoadAsync(batch, ct);
                logger.LogDebug("Batch flushed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to flush batch of {Count} entries", batch.Count);
                throw;
            }
        }

        private string GetValidationErrorMessage(ValidationResult result)
        {
            return result.Reason ?? "Validation failed";
        }

        private static DictionaryEntry Preprocess(DictionaryEntry entry)
        {
            if (entry == null)
                return null;

            var cleanedWord = Helper.DomainMarkerStripper.Strip(entry.Word ?? string.Empty);

            // ✅ FIX: Handle empty/null word
            if (string.IsNullOrWhiteSpace(cleanedWord))
            {
                return new DictionaryEntry
                {
                    Word = string.Empty,
                    NormalizedWord = string.Empty,
                    PartOfSpeech = entry.PartOfSpeech,
                    Definition = entry.Definition,
                    Etymology = entry.Etymology,
                    SenseNumber = entry.SenseNumber,
                    SourceCode = entry.SourceCode,
                    CreatedUtc = entry.CreatedUtc,
                    RawFragment = entry.RawFragment
                };
            }

            var language = Helper.LanguageDetect(cleanedWord);
            var normalized = Helper.NormalizedWordSanitize(cleanedWord, language);

            // ✅ FIX: Check eligibility and handle appropriately
            if (!Helper.IsCanonicalEligible(normalized))
            {
                normalized = string.Empty;
            }

            return new DictionaryEntry
            {
                Word = cleanedWord,
                NormalizedWord = normalized,
                PartOfSpeech = entry.PartOfSpeech,
                Definition = entry.Definition,
                Etymology = entry.Etymology,
                SenseNumber = entry.SenseNumber,
                SourceCode = entry.SourceCode,
                CreatedUtc = entry.CreatedUtc,
                RawFragment = entry.RawFragment
            };
        }
    }
}