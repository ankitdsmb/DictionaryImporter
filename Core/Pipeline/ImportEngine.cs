using Microsoft.ML;
using LanguageDetector = DictionaryImporter.Core.PreProcessing.LanguageDetector;

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
        private const int BatchSize = 10000;

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

                // Extract raw entries
                await foreach (var rawEntry in extractor.ExtractAsync(stream, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Transform each raw entry to DictionaryEntry
                    var transformedEntries = transformer.Transform(rawEntry);

                    foreach (var entry in transformedEntries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // VALIDATE each entry
                        var validationResult = validator.Validate(entry);

                        if (validationResult.IsValid)
                        {
                            entries.Add(entry);
                            logger.LogDebug("Added valid entry: {Word}", entry.Word);
                        }
                        else
                        {
                            var errorMessage = GetValidationErrorMessage(validationResult);
                            logger.LogDebug("Skipped invalid entry: {Word} - {Reason}",
                                entry.Word, errorMessage);
                        }

                        // Check batch size and load if needed
                        if (entries.Count >= BatchSize)
                        {
                            await loader.LoadAsync(entries, cancellationToken);
                            entries.Clear();
                        }
                    }
                }

                // Load any remaining entries
                if (entries.Count > 0)
                {
                    await loader.LoadAsync(entries, cancellationToken);
                }

                logger.LogInformation("Import completed successfully. Total entries: {Count}",
                    entries.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import failed");
                throw;
            }
        }

        private string GetValidationErrorMessage(ValidationResult result)
        {
            return result.Reason ?? "Validation failed";
        }

        private async Task FlushBatchAsync(
            List<DictionaryEntry> batch,
            CancellationToken ct)
        {
            await loader.LoadAsync(batch, ct);
        }

        private static DictionaryEntry Preprocess(DictionaryEntry entry)
        {
            var cleanedWord =
                DomainMarkerStripper.Strip(entry.Word);

            var language =
                LanguageDetector.Detect(cleanedWord);

            var normalized =
                NormalizedWordSanitizer.Sanitize(cleanedWord, language);

            if (!CanonicalEligibility.IsEligible(normalized)) normalized = string.Empty;

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