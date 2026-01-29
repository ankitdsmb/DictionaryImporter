using DictionaryImporter.Common;
using DictionaryImporter.Infrastructure.Validation;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Pipeline;

public sealed class ImportEngine<TRaw>(
    IDataExtractor<TRaw> extractor,
    IDataTransformer<TRaw> transformer,
    IDataLoader loader,
    IDictionaryEntryValidator validator,
    ILogger<ImportEngine<TRaw>> logger)
    : IImportEngine
{
    private const int BatchSize = 2000;

    private int _totalProcessed;
    private int _totalValid;
    private int _totalInvalid;

    async Task IImportEngine.ImportAsync(Stream source, CancellationToken ct)
        => await ImportAsync(source, ct);

    public async Task ImportAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        var batch = new List<DictionaryEntry>(BatchSize);

        try
        {
            logger.LogInformation("Starting import process");

            _totalProcessed = 0;
            _totalValid = 0;
            _totalInvalid = 0;

            await foreach (var rawEntry in extractor.ExtractAsync(stream, cancellationToken)
                                                   .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var transformed = transformer.Transform(rawEntry);
                if (transformed == null)
                    continue;

                foreach (var entry in transformed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _totalProcessed++;

                    var processed = Preprocess(entry);
                    if (processed == null)
                    {
                        _totalInvalid++;
                        continue;
                    }

                    if (!validator.Validate(processed).IsValid)
                    {
                        _totalInvalid++;
                        continue;
                    }

                    batch.Add(processed);
                    _totalValid++;

                    if (batch.Count == BatchSize)
                    {
                        await loader.LoadAsync(batch, cancellationToken)
                                    .ConfigureAwait(false);
                        batch.Clear();
                    }
                }
            }

            if (batch.Count > 0)
            {
                await loader.LoadAsync(batch, cancellationToken)
                            .ConfigureAwait(false);
                batch.Clear();
            }

            logger.LogInformation(
                "Import completed successfully. Processed={Processed}, Valid={Valid}, Invalid={Invalid}",
                _totalProcessed,
                _totalValid,
                _totalInvalid);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Import failed. Stats: Processed={Processed}, Valid={Valid}, Invalid={Invalid}",
                _totalProcessed,
                _totalValid,
                _totalInvalid);
            throw;
        }
    }

    private static DictionaryEntry? Preprocess(DictionaryEntry entry)
    {
        var word = entry.Word;
        if (string.IsNullOrEmpty(word))
            return null;

        var cleanedWord = Helper.DomainMarkerStripper.Strip(word);
        if (string.IsNullOrWhiteSpace(cleanedWord))
            return null;

        var language = Helper.LanguageDetect(cleanedWord);
        var normalized = Helper.NormalizedWordSanitize(cleanedWord, language);

        if (!Helper.IsCanonicalEligible(normalized))
            return null;

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