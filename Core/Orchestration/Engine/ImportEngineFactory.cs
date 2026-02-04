using DictionaryImporter.Core.Orchestration.Engine;
using DictionaryImporter.Infrastructure.FragmentStore;
using DictionaryImporter.Infrastructure.Validation;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Orchestration.Engine;

public sealed class ImportEngineFactory<TRaw>(
    IDataExtractor<TRaw> extractor,
    IDataTransformer<TRaw> transformer,
    IDataLoader loader,
    IDictionaryImportControl importControl,
    IRawFragmentStore rawFragmentStore,
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
            importControl,
            rawFragmentStore,
            logger);
    }
}