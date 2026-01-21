using DictionaryImporter.Core.Pipeline.Steps;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Infrastructure.OneTimeTasks;
using DictionaryImporter.Infrastructure.Persistence.Batched;
using DictionaryImporter.Sources.Century21.Parsing;
using DictionaryImporter.Sources.Collins.Parsing;
using DictionaryImporter.Sources.Common.Parsing;
using DictionaryImporter.Sources.EnglishChinese;
using DictionaryImporter.Sources.EnglishChinese.Parsing;
using DictionaryImporter.Sources.Kaikki.Parsing;
using DictionaryImporter.Sources.Oxford.Extractor;
using DictionaryImporter.Sources.Oxford.Parsing;
using DictionaryImporter.Sources.StructuredJson.Parsing;
using Microsoft.Extensions.DependencyInjection;

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

            // ✅ FACTORY DELEGATE REGISTRATIONS (keep these)
            services.AddSingleton<Func<ImportEngineFactory<KaikkiRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<KaikkiRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<GutenbergRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<GutenbergRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<StructuredJsonRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<StructuredJsonRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<EnglishChineseRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<EnglishChineseRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<CollinsRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<CollinsRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<OxfordRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<OxfordRawEntry>>);
            services.AddSingleton<Func<ImportEngineFactory<Century21RawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<Century21RawEntry>>);

            services.AddSingleton<IImportEngineRegistry, ImportEngineRegistry>();

            // ✅ EXTRACTOR REGISTRATIONS (keep these)
            services.AddSingleton<IEtymologyExtractor, OxfordEtymologyExtractor>();
            services.AddSingleton<IExampleExtractor, OxfordExampleExtractor>();
            services.AddSingleton<ISynonymExtractor, OxfordSynonymExtractor>();

            // ✅ KEEP: Register default validator (for fallback)
            services.AddSingleton<IDictionaryEntryValidator, DefaultDictionaryEntryValidator>();

            // ✅ ADD BACK: Func that returns default validator
            services.AddSingleton<Func<IDictionaryEntryValidator>>(sp => () => sp.GetRequiredService<IDictionaryEntryValidator>());
            services.AddSingleton<IDictionaryEntryValidator, DefaultDictionaryEntryValidator>();

            services.AddSingleton<Func<IDataMergeExecutor>>(sp => sp.GetRequiredService<IDataMergeExecutor>);
            services.AddSingleton<IDataMergeExecutor>(sp => new SqlDictionaryEntryMergeExecutor(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

            services.AddSingleton<DictionaryEntryLinguisticEnricher>(sp => new DictionaryEntryLinguisticEnricher(
                connectionString,
                sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                sp.GetRequiredService<ILogger<DictionaryEntryLinguisticEnricher>>()));

            services.AddSingleton<CanonicalWordIpaEnricher>(sp => new CanonicalWordIpaEnricher(
                connectionString,
                sp.GetRequiredService<SqlCanonicalWordPronunciationWriter>(),
                sp.GetRequiredService<ILogger<CanonicalWordIpaEnricher>>()));

            services.AddSingleton<CanonicalWordSyllableEnricher>(sp => new CanonicalWordSyllableEnricher(
                connectionString,
                sp.GetRequiredService<ILogger<CanonicalWordSyllableEnricher>>()));

            services.AddSingleton<IpaVerificationReporter>(sp => new IpaVerificationReporter(
                connectionString,
                sp.GetRequiredService<ILogger<IpaVerificationReporter>>()));

            services.AddSingleton<OrthographicSyllableRuleResolver>();
            services.AddSingleton<CanonicalWordOrthographicSyllableEnricher>(sp => new CanonicalWordOrthographicSyllableEnricher(
                connectionString,
                sp.GetRequiredService<ILogger<CanonicalWordOrthographicSyllableEnricher>>()));

            services.AddSingleton<IOneTimeDatabaseTask>(
                new EditorialIpaMigrationTask(connectionString));
            services.AddSingleton<IOneTimeDatabaseTask>(
                new PromoteIpaFromNotesTask(connectionString));

            // ✅ PARSER REGISTRATIONS (keep these)
            services.AddSingleton<ISourceDictionaryDefinitionParser, KaikkiDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, GutenbergDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, Century21DefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, CollinsDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, EnglishChineseEnhancedParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, OxfordDefinitionParser>();
            services.AddSingleton<ISourceDictionaryDefinitionParser, StructuredJsonDefinitionParser>();

            // ✅ RESOLVER REGISTRATION (keep this)
            services.AddSingleton<IDictionaryDefinitionParserResolver, DictionaryDefinitionParserResolver>();

            services.AddSingleton<OneTimeTaskRunner>();
            services.AddScoped<IAiAnnotationRepository, SqlAiAnnotationRepository>();
            services.AddScoped<AiEnhancementStep>();

            services.Configure<ImportPipelineOptions>(configuration.GetSection("ImportPipeline"));
            services.AddScoped<ImportPipelineOrderResolver>();
            services.AddScoped<ImportPipelineRunner>();

            // ✅ PIPELINE STEP REGISTRATIONS (keep these)
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

            // FIXED: Add proper registrations with all required parameters
            services.AddTransient<IDictionaryEntryExampleWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlDictionaryEntryExampleWriter(connectionString, batcher, logger);
            });

            services.AddTransient<SqlParsedDefinitionWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlParsedDefinitionWriter(connectionString, batcher, logger);
            });

            // ✅ Register IEntryEtymologyWriter implementation
            services.AddSingleton<IEntryEtymologyWriter>(sp =>
                new SqlDictionaryEntryEtymologyWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

            // ✅ Register IDictionaryEntryVariantWriter implementation
            services.AddSingleton<IDictionaryEntryVariantWriter>(sp =>
                new SqlDictionaryEntryVariantWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryVariantWriter>>()));

            // ✅ Register concrete SqlDictionaryEntryVariantWriter for backward compatibility
            services.AddSingleton<SqlDictionaryEntryVariantWriter>(sp =>
                new SqlDictionaryEntryVariantWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryVariantWriter>>()));

            // ✅ FIXED: Register DictionaryParsedDefinitionProcessor as CONCRETE type
            services.AddScoped<DictionaryParsedDefinitionProcessor>(sp =>
            {
                return new DictionaryParsedDefinitionProcessor(
                    connectionString,
                    sp.GetRequiredService<IDictionaryDefinitionParserResolver>(),
                    sp.GetRequiredService<SqlParsedDefinitionWriter>(),
                    sp.GetRequiredService<IDictionaryEntryCrossReferenceWriter>(),
                    sp.GetRequiredService<IDictionaryEntryAliasWriter>(),
                    sp.GetRequiredService<IEntryEtymologyWriter>(),
                    sp.GetRequiredService<IDictionaryEntryVariantWriter>(),
                    sp.GetRequiredService<IDictionaryEntryExampleWriter>(),
                    sp.GetRequiredService<IExampleExtractorRegistry>(),
                    sp.GetRequiredService<ISynonymExtractorRegistry>(),
                    sp.GetRequiredService<IDictionaryEntrySynonymWriter>(),
                    sp.GetRequiredService<IEtymologyExtractorRegistry>(),
                    sp.GetRequiredService<IDictionaryTextFormatter>(),
                    sp.GetRequiredService<IGrammarEnrichedTextService>(),
                    sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()
                );
            });

            // ✅ ALSO register as interface
            services.AddScoped<IParsedDefinitionProcessor>(sp =>
                sp.GetRequiredService<DictionaryParsedDefinitionProcessor>());


        }

        public static PipelineMode ResolvePipelineMode(IConfiguration configuration)
        {
            return configuration["Pipeline:Mode"] == "ImportOnly"
                ? PipelineMode.ImportOnly
                : PipelineMode.Full;
        }
    }
}
