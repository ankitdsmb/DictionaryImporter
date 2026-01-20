using DictionaryImporter.Core.Pipeline.Steps;
using DictionaryImporter.Infrastructure.OneTimeTasks;
using DictionaryImporter.Sources.Century21.Parsing;
using DictionaryImporter.Sources.Collins.Parsing;
using DictionaryImporter.Sources.Common.Parsing;
using DictionaryImporter.Sources.EnglishChinese.Parsing;
using DictionaryImporter.Sources.Kaikki.Parsing;
using DictionaryImporter.Sources.Oxford.Parsing;
using DictionaryImporter.Sources.StructuredJson.Parsing;

namespace DictionaryImporter.Bootstrap
{
    public static class BootstrapPipeline
    {
        public static void Register(IServiceCollection services, IConfiguration configuration)
        {
            var connectionString =
                configuration.GetConnectionString("DictionaryImporter")
                ?? throw new InvalidOperationException(
                    "Connection string 'DictionaryImporter' not configured");

            services.AddSingleton<ImportEngineFactory<KaikkiRawEntry>>();
            services.AddSingleton<ImportEngineFactory<GutenbergRawEntry>>();
            services.AddSingleton<ImportEngineFactory<StructuredJsonRawEntry>>();
            services.AddSingleton<ImportEngineFactory<EnglishChineseRawEntry>>();
            services.AddSingleton<ImportEngineFactory<CollinsRawEntry>>();
            services.AddSingleton<ImportEngineFactory<OxfordRawEntry>>();
            services.AddSingleton<ImportEngineFactory<Century21RawEntry>>();

            services.AddSingleton<Func<ImportEngineFactory<KaikkiRawEntry>>>(sp =>
                sp.GetRequiredService<ImportEngineFactory<KaikkiRawEntry>>);

            services.AddSingleton<Func<ImportEngineFactory<GutenbergRawEntry>>>(sp =>
                sp.GetRequiredService<ImportEngineFactory<GutenbergRawEntry>>);

            services.AddSingleton<Func<ImportEngineFactory<StructuredJsonRawEntry>>>(sp =>
                sp.GetRequiredService<ImportEngineFactory<StructuredJsonRawEntry>>);

            services.AddSingleton<Func<ImportEngineFactory<EnglishChineseRawEntry>>>(sp =>
                sp.GetRequiredService<ImportEngineFactory<EnglishChineseRawEntry>>);

            services.AddSingleton<Func<ImportEngineFactory<CollinsRawEntry>>>(sp =>
                sp.GetRequiredService<ImportEngineFactory<CollinsRawEntry>>);

            services.AddSingleton<Func<ImportEngineFactory<OxfordRawEntry>>>(sp =>
                sp.GetRequiredService<ImportEngineFactory<OxfordRawEntry>>);

            services.AddSingleton<Func<ImportEngineFactory<Century21RawEntry>>>(sp =>
                sp.GetRequiredService<ImportEngineFactory<Century21RawEntry>>);

            services.AddSingleton<IImportEngineRegistry, ImportEngineRegistry>();

            services.AddSingleton<IEtymologyExtractor, OxfordEtymologyExtractor>();
            services.AddSingleton<IExampleExtractor, OxfordExampleExtractor>();
            services.AddSingleton<ISynonymExtractor, OxfordSynonymExtractor>();

            services.AddSingleton<Func<IDictionaryEntryValidator>>(sp =>
                sp.GetRequiredService<IDictionaryEntryValidator>);

            services.AddSingleton<Func<IDataMergeExecutor>>(sp =>
                sp.GetRequiredService<IDataMergeExecutor>);

            services.AddSingleton<IDataMergeExecutor>(sp =>
                new SqlDictionaryEntryMergeExecutor(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

            services.AddSingleton<DictionaryEntryLinguisticEnricher>(sp =>
                new DictionaryEntryLinguisticEnricher(
                    connectionString,
                    sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                    sp.GetRequiredService<ILogger<DictionaryEntryLinguisticEnricher>>()));

            services.AddSingleton<CanonicalWordIpaEnricher>(sp =>
                new CanonicalWordIpaEnricher(
                    connectionString,
                    sp.GetRequiredService<SqlCanonicalWordPronunciationWriter>(),
                    sp.GetRequiredService<ILogger<CanonicalWordIpaEnricher>>()));

            services.AddSingleton<CanonicalWordSyllableEnricher>(sp =>
                new CanonicalWordSyllableEnricher(
                    connectionString,
                    sp.GetRequiredService<ILogger<CanonicalWordSyllableEnricher>>()));

            services.AddSingleton<IpaVerificationReporter>(sp =>
                new IpaVerificationReporter(
                    connectionString,
                    sp.GetRequiredService<ILogger<IpaVerificationReporter>>()));

            services.AddSingleton<OrthographicSyllableRuleResolver>();

            services.AddSingleton<CanonicalWordOrthographicSyllableEnricher>(sp =>
                new CanonicalWordOrthographicSyllableEnricher(
                    connectionString,
                    sp.GetRequiredService<ILogger<CanonicalWordOrthographicSyllableEnricher>>()));

            services.AddSingleton<IOneTimeDatabaseTask>(
                new EditorialIpaMigrationTask(connectionString));

            services.AddSingleton<IOneTimeDatabaseTask>(
                new PromoteIpaFromNotesTask(connectionString));

            // Register ALL source parsers
            services.AddSingleton<ISourceDictionaryDefinitionParser, KaikkiDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, GutenbergDefinitionParser>();

            services.AddSingleton<ISourceDictionaryDefinitionParser, Century21DefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, CollinsDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, EnglishChineseEnhancedParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, OxfordDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, StructuredJsonDefinitionParser>();

            // Resolver
            services.AddSingleton<IDictionaryDefinitionParserResolver, DictionaryDefinitionParserResolver>();

            services.AddSingleton<OneTimeTaskRunner>();

            services.AddScoped<IAiAnnotationRepository, SqlAiAnnotationRepository>();
            services.AddScoped<AiEnhancementStep>();

            services.Configure<ImportPipelineOptions>(configuration.GetSection("ImportPipeline"));

            services.AddScoped<ImportPipelineOrderResolver>();
            services.AddScoped<ImportPipelineRunner>();

            services.AddScoped<IImportPipelineStep, CanonicalizationPipelineStep>();
            services.AddScoped<IImportPipelineStep, ParsingPipelineStep>();
            services.AddScoped<IImportPipelineStep, LinguisticsPipelineStep>();
            services.AddScoped<IImportPipelineStep, GrammarCorrectionPipelineStep>();
            services.AddScoped<IImportPipelineStep, AiEnhancementPipelineStep>();
            services.AddScoped<IImportPipelineStep, OrthographicSyllablesPipelineStep>();
            services.AddScoped<IImportPipelineStep, GraphBuildPipelineStep>();
            services.AddScoped<IImportPipelineStep, GraphValidationPipelineStep>();
            services.AddScoped<IImportPipelineStep, ConceptBuildPipelineStep>();
            services.AddScoped<IImportPipelineStep, ConceptMergePipelineStep>();
            services.AddScoped<IImportPipelineStep, IpaPipelineStep>();
            services.AddScoped<IImportPipelineStep, IpaSyllablesPipelineStep>();
            services.AddScoped<IImportPipelineStep, VerificationPipelineStep>();

            services.AddScoped<IParsedDefinitionProcessor, DictionaryParsedDefinitionProcessor>();

            foreach (var qa in KnownQaChecks.CreateAll(connectionString))
                services.AddSingleton<IQaCheck>(qa);

            services.AddSingleton<QaRunner>();

            services.AddScoped<ImportOrchestrator>();
        }

        public static PipelineMode ResolvePipelineMode(IConfiguration configuration)
        {
            return configuration["Pipeline:Mode"] == "ImportOnly"
                ? PipelineMode.ImportOnly
                : PipelineMode.Full;
        }
    }
}