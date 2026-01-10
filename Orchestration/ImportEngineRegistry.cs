using DictionaryImporter.Core.Pipeline;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Sources.EnglishChinese;
using DictionaryImporter.Sources.Gutenberg;
using DictionaryImporter.Sources.StructuredJson;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Orchestration
{
    public sealed class ImportEngineRegistry : IImportEngineRegistry
    {
        private readonly Func<ImportEngineFactory<GutenbergRawEntry>> _gutFactory;
        private readonly Func<ImportEngineFactory<StructuredJsonRawEntry>> _jsonFactory;
        private readonly Func<ImportEngineFactory<EnglishChineseRawEntry>> _engChnFactory;
        private readonly ILogger<ImportEngineRegistry> _logger;

        public ImportEngineRegistry(
            Func<ImportEngineFactory<GutenbergRawEntry>> gutFactory,
            Func<ImportEngineFactory<StructuredJsonRawEntry>> jsonFactory,
            Func<ImportEngineFactory<EnglishChineseRawEntry>> engChnFactory,
            ILogger<ImportEngineRegistry> logger)
        {
            _gutFactory = gutFactory;
            _jsonFactory = jsonFactory;
            _engChnFactory = engChnFactory;
            _logger = logger;
        }

        public IImportEngine CreateEngine(
            string sourceCode,
            IDictionaryEntryValidator validator)
        {
            _logger.LogInformation(
                "ImportEngine selection | SourceCode={SourceCode}",
                sourceCode);

            return sourceCode switch
            {
                "GUT_WEBSTER" =>
                    CreateAndLog(
                        sourceCode,
                        "Gutenberg",
                        _gutFactory,
                        validator),

                "STRUCT_JSON" =>
                    CreateAndLog(
                        sourceCode,
                        "StructuredJson",
                        _jsonFactory,
                        validator),

                // =====================================================
                // ENG_CHN — English–Chinese Dictionary
                // =====================================================
                "ENG_CHN" =>
                    CreateAndLog(
                        sourceCode,
                        "EnglishChinese",
                        _engChnFactory,
                        validator),

                _ => ThrowUnknownSource(sourceCode)
            };
        }

        private IImportEngine CreateAndLog<TRaw>(
            string sourceCode,
            string engineName,
            Func<ImportEngineFactory<TRaw>> factory,
            IDictionaryEntryValidator validator)
        {
            _logger.LogInformation(
                "ImportEngine resolved | SourceCode={SourceCode} | Engine={Engine}",
                sourceCode,
                engineName);

            return factory().Create(validator);
        }

        private static IImportEngine ThrowUnknownSource(
            string sourceCode)
        {
            throw new InvalidOperationException(
                $"No import engine registered for source '{sourceCode}'");
        }
    }
}