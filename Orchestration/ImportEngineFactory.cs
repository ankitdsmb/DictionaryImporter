namespace DictionaryImporter.Orchestration;

public class ImportEngineFactory<TRaw>(
    IDataExtractor<TRaw> extractor,
    IDataTransformer<TRaw> transformer,
    IDataLoader loader,
    ILogger<ImportEngine<TRaw>> logger)
{
    public ImportEngine<TRaw> Create(
        IDictionaryEntryValidator validator)
    {
        logger.LogInformation(
            "ImportEngine created | RawType={RawType} | Validator={Validator}",
            typeof(TRaw).Name,
            validator.GetType().Name);

        return new ImportEngine<TRaw>(
            extractor,
            transformer,
            loader,
            validator,
            logger);
    }
}