using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Pipeline
{
    public sealed class ImportEngine<TRaw>
    {
        private const int BatchSize = 10000;

        private readonly IDataExtractor<TRaw> _extractor;
        private readonly IDataTransformer<TRaw> _transformer;
        private readonly IDataLoader _loader;
        private readonly IDictionaryEntryValidator _validator;
        private readonly IEntryEtymologyWriter _etymologyWriter;
        private readonly ILogger<ImportEngine<TRaw>> _logger;

        public ImportEngine(
            IDataExtractor<TRaw> extractor,
            IDataTransformer<TRaw> transformer,
            IDataLoader loader,
            IDictionaryEntryValidator validator,
            IEntryEtymologyWriter etymologyWriter,
            ILogger<ImportEngine<TRaw>> logger)
        {
            _extractor = extractor;
            _transformer = transformer;
            _loader = loader;
            _validator = validator;
            _etymologyWriter = etymologyWriter;
            _logger = logger;
        }

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

                foreach (var entry in _transformer.Transform(raw))
                {
                    ct.ThrowIfCancellationRequested();

                    // ----------------------------------------------------
                    // VALIDATION
                    // ----------------------------------------------------
                    var result = _validator.Validate(entry);

                    if (!result.IsValid)
                    {
                        _logger.LogWarning(
                            "Entry rejected | Word={Word} | Source={Source} | Reason={Reason}",
                            entry.Word,
                            entry.SourceCode,
                            result.Reason);

                        metrics.IncrementRejected();
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

            return metrics;
        }

        private async Task FlushBatchAsync(
            List<DictionaryEntry> batch,
            CancellationToken ct)
        {
            await _loader.LoadAsync(batch, ct);
        }
    }
}
