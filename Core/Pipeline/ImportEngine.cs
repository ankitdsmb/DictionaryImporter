using LanguageDetector = DictionaryImporter.Core.PreProcessing.LanguageDetector;

namespace DictionaryImporter.Core.Pipeline;

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

    public async Task<ImportMetrics> ImportAsync(
        Stream source,
        CancellationToken ct)
    {
        var metrics = new ImportMetrics();
        metrics.Start();

        var batch = new List<DictionaryEntry>(BatchSize);

        await foreach (var raw in extractor.ExtractAsync(source, ct))
        {
            metrics.IncrementRaw();

            foreach (var rawEntry in transformer.Transform(raw))
            {
                ct.ThrowIfCancellationRequested();

                var entry = Preprocess(rawEntry);

                var result = validator.Validate(entry);

                if (!result.IsValid)
                {
                    if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
                        metrics.IncrementCanonicalEligibilityRejected();
                    else
                        metrics.IncrementValidatorRejected();

                    logger.LogWarning(
                        "Entry rejected | Word={Word} | Source={Source} | Reason={Reason}",
                        entry.Word,
                        entry.SourceCode,
                        result.Reason);

                    continue;
                }

                batch.Add(entry);
                metrics.AddTransformed(1);

                if (batch.Count >= BatchSize)
                {
                    await FlushBatchAsync(batch, ct);
                    batch.Clear();
                }
            }
        }

        if (batch.Count > 0)
            await FlushBatchAsync(batch, ct);

        metrics.Stop();

        logger.LogInformation(
            "ETL completed | Raw={Raw} | Accepted={Accepted} | Rejected={Rejected} | Duration={Ms}ms",
            metrics.RawEntriesExtracted,
            metrics.EntriesStaged,
            metrics.EntriesRejected,
            metrics.Duration.TotalMilliseconds);

        logger.LogInformation(
            "Rejection breakdown | CanonicalEligibility={Canonical} | Validator={Validator}",
            metrics.RejectedByCanonicalEligibility,
            metrics.RejectedByValidator);

        return metrics;
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
            CreatedUtc = entry.CreatedUtc
        };
    }
}