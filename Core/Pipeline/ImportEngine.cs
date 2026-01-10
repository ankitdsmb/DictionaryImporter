using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Pipeline
{
    public sealed class ImportEngine<TRaw> : IImportEngine
    {
        private const int BatchSize = 10000;

        private readonly IDataExtractor<TRaw> _extractor;
        private readonly IDataTransformer<TRaw> _transformer;
        private readonly IDataLoader _loader;
        private readonly IDictionaryEntryValidator _validator;
        private readonly ILogger<ImportEngine<TRaw>> _logger;

        public ImportEngine(
            IDataExtractor<TRaw> extractor,
            IDataTransformer<TRaw> transformer,
            IDataLoader loader,
            IDictionaryEntryValidator validator,
            ILogger<ImportEngine<TRaw>> logger)
        {
            _extractor = extractor;
            _transformer = transformer;
            _loader = loader;
            _validator = validator;
            _logger = logger;
        }

        // ============================================================
        // MAIN IMPORT (RETURNS METRICS)
        // ============================================================
        public async Task<ImportMetrics> ImportAsync(
            Stream source,
            CancellationToken ct)
        {
            var metrics = new ImportMetrics();
            metrics.Start();

            var batch = new List<DictionaryEntry>(BatchSize);

            await foreach (var raw in _extractor.ExtractAsync(source, ct))
            {
                metrics.IncrementRaw();

                foreach (var rawEntry in _transformer.Transform(raw))
                {
                    ct.ThrowIfCancellationRequested();

                    // =====================================================
                    // PRE-CANONICAL SANITIZATION
                    // =====================================================
                    var entry = Preprocess(rawEntry);

                    // ---------------- VALIDATION ----------------
                    var result = _validator.Validate(entry);

                    if (!result.IsValid)
                    {
                        if (string.IsNullOrWhiteSpace(entry.NormalizedWord))
                        {
                            metrics.IncrementCanonicalEligibilityRejected();
                        }
                        else
                        {
                            metrics.IncrementValidatorRejected();
                        }

                        _logger.LogWarning(
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

            _logger.LogInformation(
                "ETL completed | Raw={Raw} | Accepted={Accepted} | Rejected={Rejected} | Duration={Ms}ms",
                metrics.RawEntriesExtracted,
                metrics.EntriesStaged,
                metrics.EntriesRejected,
                metrics.Duration.TotalMilliseconds);

            _logger.LogInformation(
                "Rejection breakdown | CanonicalEligibility={Canonical} | Validator={Validator}",
                metrics.RejectedByCanonicalEligibility,
                metrics.RejectedByValidator);

            return metrics;
        }

        private async Task FlushBatchAsync(
            List<DictionaryEntry> batch,
            CancellationToken ct)
        {
            await _loader.LoadAsync(batch, ct);
        }

        // ============================================================
        // EXPLICIT INTERFACE IMPLEMENTATION
        // ============================================================
        async Task IImportEngine.ImportAsync(
            Stream source,
            CancellationToken ct)
        {
            await ImportAsync(source, ct);
        }

        // ============================================================
        // PREPROCESSING PIPELINE
        // ============================================================
        private static DictionaryEntry Preprocess(DictionaryEntry entry)
        {
            // 1. Strip domain markers
            var cleanedWord =
                DomainMarkerStripper.Strip(entry.Word);

            // 2. Detect language
            var language =
                LanguageDetector.Detect(cleanedWord);

            // 3. Normalize safely
            var normalized =
                NormalizedWordSanitizer.Sanitize(cleanedWord, language);

            // 4. Canonical eligibility gate (CRITICAL)
            if (!CanonicalEligibility.IsEligible(normalized))
            {
                normalized = string.Empty; // forces validator rejection
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
                CreatedUtc = entry.CreatedUtc
            };
        }
    }
}
