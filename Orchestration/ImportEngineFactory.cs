using DictionaryImporter.Core.Pipeline;

namespace DictionaryImporter.Orchestration;

public sealed class ImportEngineFactory<TRaw>
{
    private readonly IDataExtractor<TRaw> _extractor;
    private readonly IDataLoader _loader;
    private readonly ILogger<ImportEngine<TRaw>> _logger;
    private readonly IDataTransformer<TRaw> _transformer;

    public ImportEngineFactory(
        IDataExtractor<TRaw> extractor,
        IDataTransformer<TRaw> transformer,
        IDataLoader loader,
        ILogger<ImportEngine<TRaw>> logger)
    {
        _extractor = extractor;
        _transformer = transformer;
        _loader = loader;
        _logger = logger;
    }

    public ImportEngine<TRaw> Create(
        IDictionaryEntryValidator validator)
    {
        _logger.LogInformation(
            "ImportEngine created | RawType={RawType} | Validator={Validator}",
            typeof(TRaw).Name,
            validator.GetType().Name);

        return new ImportEngine<TRaw>(
            _extractor,
            _transformer,
            _loader,
            validator,
            _logger);
    }
}