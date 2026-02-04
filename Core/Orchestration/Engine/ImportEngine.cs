using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.FragmentStore;

namespace DictionaryImporter.Core.Orchestration.Engine;

public sealed class ImportEngine<TRaw>(IDataExtractor<TRaw> extractor, IDataTransformer<TRaw> transformer, IDataLoader loader, IDictionaryEntryValidator validator, IDictionaryImportControl importControl, IRawFragmentStore rawFragmentStore, ILogger<ImportEngine<TRaw>> logger) : IImportEngine
{
    private const int BatchSize = 2000;

    async Task IImportEngine.ImportAsync(Stream source, CancellationToken ct)
        => await ImportAsync(source, ct);

    public async Task ImportAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var batch = new List<DictionaryEntry>(BatchSize);
        string? sourceCode = null;

        try
        {
            logger.LogInformation("Starting import process");

            await foreach (var rawEntry in extractor.ExtractAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var transformed = transformer.Transform(rawEntry);
                if (transformed == null)
                    continue;

                foreach (var entry in transformed)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    sourceCode ??= entry.SourceCode;

                    var processed = Preprocess(entry);
                    if (processed == null)
                        continue;

                    if (!validator.Validate(processed).IsValid)
                        continue;

                    batch.Add(processed);

                    if (batch.Count == BatchSize)
                    {
                        await loader.LoadAsync(batch, cancellationToken).ConfigureAwait(false);
                        batch.Clear();
                    }
                }
            }

            if (batch.Count > 0)
            {
                await loader.LoadAsync(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }

            if (string.IsNullOrWhiteSpace(sourceCode))
                return;

            await importControl.MarkSourceCompletedAsync(sourceCode, cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Source import completed: {SourceCode}", sourceCode);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            linkedCts.CancelAfter(TimeSpan.FromMinutes(10));

            await importControl.TryFinalizeAsync(sourceCode, linkedCts.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import failed");
            throw;
        }
    }

    private DictionaryEntry? Preprocess(DictionaryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Word))
            return null;
        var cleanedWord = Helper.DomainMarkerStripper.Strip(entry.Word);
        if (string.IsNullOrWhiteSpace(cleanedWord))
            return null;
        var language = Helper.LanguageDetect(cleanedWord);
        var normalized = Helper.NormalizedWordSanitize(cleanedWord, language);
        if (!Helper.IsCanonicalEligible(normalized))
            return null;
        var rawFragmentId = rawFragmentStore.Save(entry.SourceCode, entry.RawFragmentLine, Encoding.UTF8, cleanedWord);
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
            RawFragment = rawFragmentId
        };
    }
}