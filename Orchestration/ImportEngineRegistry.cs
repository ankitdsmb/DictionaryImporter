using DictionaryImporter.Core.Pipeline;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Sources.Collins.Models;
using DictionaryImporter.Sources.EnglishChinese.Models;
using DictionaryImporter.Sources.Gutenberg.Models;
using DictionaryImporter.Sources.Oxford.Models;
using DictionaryImporter.Sources.StructuredJson.Models;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Orchestration
{
    public sealed class ImportEngineRegistry : IImportEngineRegistry
    {
        private readonly Func<ImportEngineFactory<GutenbergRawEntry>> _gutFactory;
        private readonly Func<ImportEngineFactory<StructuredJsonRawEntry>> _jsonFactory;
        private readonly Func<ImportEngineFactory<EnglishChineseRawEntry>> _engChnFactory;
        private readonly Func<ImportEngineFactory<CollinsRawEntry>> _collinsFactory;
        private readonly Func<ImportEngineFactory<OxfordRawEntry>> _oxfordFactory;
        private readonly ILogger<ImportEngineRegistry> _logger;

        public ImportEngineRegistry(
            Func<ImportEngineFactory<GutenbergRawEntry>> gutFactory,
            Func<ImportEngineFactory<StructuredJsonRawEntry>> jsonFactory,
            Func<ImportEngineFactory<EnglishChineseRawEntry>> engChnFactory,
            Func<ImportEngineFactory<CollinsRawEntry>> collinsFactory,
            Func<ImportEngineFactory<OxfordRawEntry>> oxfordsFactory,
            ILogger<ImportEngineRegistry> logger)
        {
            _gutFactory = gutFactory;
            _jsonFactory = jsonFactory;
            _engChnFactory = engChnFactory;
            _collinsFactory = collinsFactory;
            _oxfordFactory = oxfordsFactory;
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
                // =====================================================
                // GUT_WEBSTER — Gutenberg Webster Dictionary
                // =====================================================
                "GUT_WEBSTER" =>
                    CreateAndLog(
                        sourceCode,
                        "Gutenberg",
                        _gutFactory,
                        validator),

                // =====================================================
                // STRUCT_JSON — Structured JSON Dictionary
                // =====================================================
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

                // =====================================================
                // ENG_COLLINS — Collins English Dictionary
                // =====================================================
                "ENG_COLLINS" =>
                    CreateAndLog(
                        sourceCode,
                        "Collins",
                        _collinsFactory,
                        validator),

                // =====================================================
                // ENG_OXFORD — Oxford English Dictionary
                // =====================================================
                "ENG_OXFORD" =>
                    CreateAndLog(
                        sourceCode,
                        "Oxford",
                        _oxfordFactory,
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