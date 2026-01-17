namespace DictionaryImporter.Orchestration
{
    public sealed class ImportEngineRegistry(
        Func<ImportEngineFactory<GutenbergRawEntry>> gutFactory,
        Func<ImportEngineFactory<StructuredJsonRawEntry>> jsonFactory,
        Func<ImportEngineFactory<EnglishChineseRawEntry>> engChnFactory,
        Func<ImportEngineFactory<CollinsRawEntry>> collinsFactory,
        Func<ImportEngineFactory<OxfordRawEntry>> oxfordsFactory,
        Func<ImportEngineFactory<Century21RawEntry>> country21Factory,
        Func<ImportEngineFactory<KaikkiRawEntry>> kaikkiFactory,
        ILogger<ImportEngineRegistry> logger) : IImportEngineRegistry
    {
        public IImportEngine CreateEngine(
            string sourceCode,
            IDictionaryEntryValidator validator)
        {
            logger.LogInformation(
                "ImportEngine selection | SourceCode={SourceCode}",
                sourceCode);

            return sourceCode switch
            {
                "GUT_WEBSTER" =>
                    CreateAndLog(
                        sourceCode,
                        "Gutenberg",
                        gutFactory,
                        validator),

                "STRUCT_JSON" =>
                    CreateAndLog(
                        sourceCode,
                        "StructuredJson",
                        jsonFactory,
                        validator),

                "ENG_CHN" =>
                    CreateAndLog(
                        sourceCode,
                        "EnglishChinese",
                        engChnFactory,
                        validator),

                "ENG_COLLINS" =>
                    CreateAndLog(
                        sourceCode,
                        "Collins",
                        collinsFactory,
                        validator),

                "ENG_OXFORD" =>
                    CreateAndLog(
                        sourceCode,
                        "Oxford",
                        oxfordsFactory,
                        validator),

                "CENTURY21" =>
                    CreateAndLog(
                        sourceCode,
                        "Century21",
                        country21Factory,
                        validator),

                "KAIKKI" =>
                    CreateAndLog(
                        sourceCode,
                        "Kaikki",
                        kaikkiFactory,
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
            logger.LogInformation(
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