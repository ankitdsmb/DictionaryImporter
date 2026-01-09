using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Pipeline;
using DictionaryImporter.Core.Validation;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Orchestration
{
    public sealed class ImportEngineFactory<TRaw>
    {
        private readonly IDataExtractor<TRaw> _extractor;
        private readonly IDataTransformer<TRaw> _transformer;
        private readonly IDataLoader _loader;
        private readonly IEntryEtymologyWriter _etymologyWriter;
        private readonly ILogger<ImportEngine<TRaw>> _logger;

        public ImportEngineFactory(
            IDataExtractor<TRaw> extractor,
            IDataTransformer<TRaw> transformer,
            IDataLoader loader,
            IEntryEtymologyWriter etymologyWriter,
            ILogger<ImportEngine<TRaw>> logger)
        {
            _extractor = extractor;
            _transformer = transformer;
            _loader = loader;
            _etymologyWriter = etymologyWriter;
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
                _etymologyWriter,
                _logger);
        }
    }
}
