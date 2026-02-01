using DictionaryImporter.Core.Orchestration;
using DictionaryImporter.Core.Orchestration.Concurrency;
using DictionaryImporter.Core.Orchestration.Engine;
using DictionaryImporter.Core.Orchestration.Models;
using DictionaryImporter.Core.Orchestration.Pipeline;
using DictionaryImporter.Core.Orchestration.Pipeline.Steps;
using DictionaryImporter.Core.Orchestration.Sources;
using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Rewriter;
using DictionaryImporter.HostedService;
using DictionaryImporter.Infrastructure.OneTimeTasks;
using DictionaryImporter.Infrastructure.Source;
using DictionaryImporter.Infrastructure.Validation;
using DictionaryImporter.Sources.Century21.Parsing;
using DictionaryImporter.Sources.Collins.Extractor;
using DictionaryImporter.Sources.Collins.parsing;
using DictionaryImporter.Sources.EnglishChinese.Extractor;
using DictionaryImporter.Sources.EnglishChinese.Parsing;
using DictionaryImporter.Sources.Gutenberg.Extractor;
using DictionaryImporter.Sources.Kaikki.Parsing;
using DictionaryImporter.Sources.Oxford.Extractor;
using DictionaryImporter.Sources.Oxford.Parsing;
using DictionaryImporter.Sources.StructuredJson.Parsing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DictionaryImporter.Bootstrap;

public static class BootstrapPipeline
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("DictionaryImporter")
            ?? throw new InvalidOperationException(
                "Connection string 'DictionaryImporter' not configured");

        // ------------------------------------------------------------
        // ✅ PARALLEL PROCESSING SERVICES
        // ------------------------------------------------------------
        services.Configure<BatchProcessingSettings>(configuration.GetSection("BatchProcessing"));
        services.Configure<ParallelProcessingSettings>(configuration.GetSection("ParallelProcessing"));
        services.Configure<GrammarOptions>(configuration.GetSection("Grammar"));

        services.AddSingleton<ImportConcurrencyManager>();

        // Add batch collector
        services.AddScoped<IBatchProcessedDataCollector, InMemoryBatchCollector>();

        // ------------------------------------------------------------
        // ✅ PARALLEL PROCESSING SERVICES (NEW)
        // ------------------------------------------------------------
        services.AddSingleton<ImportConcurrencyManager>();
        services.AddSingleton<Func<IDataMergeExecutor>>(sp => sp.GetRequiredService<IDataMergeExecutor>);

        // ------------------------------------------------------------
        // ✅ REQUIRED: GenericSqlBatcher (used by multiple writers)
        // ------------------------------------------------------------
        services.AddSingleton<GenericSqlBatcher>(sp =>
            new GenericSqlBatcher(
                connectionString,
                sp.GetRequiredService<ILogger<GenericSqlBatcher>>()));

        // ------------------------------------------------------------
        // ✅ REQUIRED: Stored Procedure Executor
        // ------------------------------------------------------------
        services.AddSingleton<ISqlStoredProcedureExecutor>(_ =>
            new SqlStoredProcedureExecutor(connectionString));

        // ------------------------------------------------------------
        // ✅ Options Bindings
        // ------------------------------------------------------------
        services.Configure<RuleBasedRewriteExamplesOptions>(
            configuration.GetSection("RuleBasedRewrite:RewriteExamples"));
        services.Configure<RuleBasedRewriteDefinitionsOptions>(
            configuration.GetSection("RuleBasedRewrite:RewriteDefinitions"));
        services.Configure<LuceneSuggestionsOptions>(
            configuration.GetSection("LuceneSuggestions"));

        // ------------------------------------------------------------
        // ✅ FACTORY DELEGATE REGISTRATIONS (keep these)
        // ------------------------------------------------------------
        services.AddSingleton<Func<ImportEngineFactory<KaikkiRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<KaikkiRawEntry>>);
        services.AddSingleton<Func<ImportEngineFactory<GutenbergRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<GutenbergRawEntry>>);
        services.AddSingleton<Func<ImportEngineFactory<StructuredJsonRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<StructuredJsonRawEntry>>);
        services.AddSingleton<Func<ImportEngineFactory<EnglishChineseRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<EnglishChineseRawEntry>>);
        services.AddSingleton<Func<ImportEngineFactory<CollinsRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<CollinsRawEntry>>);
        services.AddSingleton<Func<ImportEngineFactory<OxfordRawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<OxfordRawEntry>>);
        services.AddSingleton<Func<ImportEngineFactory<Century21RawEntry>>>(sp => sp.GetRequiredService<ImportEngineFactory<Century21RawEntry>>);

        services.AddSingleton<IImportEngineRegistry, ImportEngineRegistry>();

        // ------------------------------------------------------------
        // ✅ EXTRACTOR REGISTRATIONS (keep these)
        // ------------------------------------------------------------
        services.AddSingleton<IEtymologyExtractor, OxfordEtymologyExtractor>();
        services.AddSingleton<IExampleExtractor, OxfordExampleExtractor>();
        services.AddSingleton<ISynonymExtractor, OxfordSynonymExtractor>();

        services.AddSingleton<IEtymologyExtractor, CollinsEtymologyExtractor>();
        services.AddSingleton<IExampleExtractor, CollinsExampleExtractor>();
        services.AddSingleton<ISynonymExtractor, CollinsSynonymExtractor>();

        services.AddSingleton<IEtymologyExtractor, EnglishChineseEtymologyExtractor>();
        services.AddSingleton<IExampleExtractor, EnglishChineseExampleExtractor>();
        services.AddSingleton<ISynonymExtractor, EnglishChineseSynonymExtractor>();

        services.AddSingleton<IEtymologyExtractor, GutenbergEtymologyExtractor>();
        services.AddSingleton<IExampleExtractor, GutenbergExampleExtractor>();
        services.AddSingleton<ISynonymExtractor, GutenbergSynonymExtractor>();

        services.AddSingleton<IEtymologyExtractor, KaikkiEtymologyExtractor>();
        services.AddSingleton<IExampleExtractor, KaikkiExampleExtractor>();
        services.AddSingleton<ISynonymExtractor, KaikkiSynonymExtractor>();

        // ------------------------------------------------------------
        // ✅ KEEP: Register default validator (for fallback)
        // ------------------------------------------------------------
        services.AddSingleton<IDictionaryEntryValidator, DefaultDictionaryEntryValidator>();
        services.AddSingleton<Func<IDictionaryEntryValidator>>(sp => () => sp.GetRequiredService<IDictionaryEntryValidator>());

        services.AddSingleton<IDataMergeExecutor>(sp =>
            new SqlDictionaryEntryMergeExecutor(
                connectionString,
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

        // ------------------------------------------------------------
        // ✅ POS REPOSITORY (ONLY ONCE)
        // ------------------------------------------------------------
        services.AddSingleton<IDictionaryEntryPartOfSpeechRepository>(sp =>
            new SqlDictionaryEntryPartOfSpeechRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlDictionaryEntryPartOfSpeechRepository>>()));
        // Program.cs or Startup.cs

        services.AddSingleton<ImportConcurrencyManager>();
        services.AddScoped<IBatchProcessedDataCollector>(sp =>
        {
            return new InMemoryBatchCollector(
                connectionString,
                sp.GetRequiredService<ILogger<InMemoryBatchCollector>>(),
                sp.GetRequiredService<IOptions<BatchProcessingSettings>>()
            );
        });

        services.AddScoped<DictionaryParsedDefinitionProcessor>();
        // ------------------------------------------------------------
        // ✅ PARSER REGISTRATIONS (keep these)
        // ------------------------------------------------------------
        services.AddSingleton<ISourceDictionaryDefinitionParser, KaikkiDefinitionParser>();
        services.AddSingleton<ISourceDictionaryDefinitionParser, GutenbergDefinitionParser>();
        services.AddSingleton<ISourceDictionaryDefinitionParser, Century21DefinitionParser>();
        services.AddSingleton<ISourceDictionaryDefinitionParser, CollinsDefinitionParser>();
        services.AddSingleton<ISourceDictionaryDefinitionParser, EnglishChineseParser>();
        services.AddSingleton<ISourceDictionaryDefinitionParser, OxfordDefinitionParser>();
        services.AddSingleton<ISourceDictionaryDefinitionParser, StructuredJsonDefinitionParser>();

        services.AddSingleton<IDictionaryDefinitionParserResolver, DictionaryDefinitionParserResolver>();

        // ------------------------------------------------------------
        // ✅ ONE-TIME TASKS
        // ------------------------------------------------------------
        services.AddSingleton<IOneTimeDatabaseTask>(new EditorialIpaMigrationTask(connectionString));
        services.AddSingleton<IOneTimeDatabaseTask>(new PromoteIpaFromNotesTask(connectionString));
        services.AddSingleton<OneTimeTaskRunner>();

        // ------------------------------------------------------------
        // ✅ LINGUISTIC SERVICES
        // ------------------------------------------------------------
        services.AddSingleton<OrthographicSyllableRuleResolver>();

        services.AddSingleton<CanonicalWordIpaEnricher>(sp => new CanonicalWordIpaEnricher(
            connectionString,
            sp.GetRequiredService<SqlCanonicalWordPronunciationWriter>(),
            sp.GetRequiredService<ILogger<CanonicalWordIpaEnricher>>()));

        services.AddSingleton<CanonicalWordSyllableEnricher>(sp => new CanonicalWordSyllableEnricher(
            connectionString,
            sp.GetRequiredService<ILogger<CanonicalWordSyllableEnricher>>()));

        services.AddSingleton<CanonicalWordOrthographicSyllableEnricher>(sp => new CanonicalWordOrthographicSyllableEnricher(
            connectionString,
            sp.GetRequiredService<ILogger<CanonicalWordOrthographicSyllableEnricher>>()));

        services.AddSingleton<IpaVerificationReporter>(sp => new IpaVerificationReporter(
            connectionString,
            sp.GetRequiredService<ILogger<IpaVerificationReporter>>()));

        // ------------------------------------------------------------
        // ✅ REWRITE SERVICES
        // ------------------------------------------------------------
        services.AddSingleton<IRewriteMapRepository>(sp =>
            new SqlRewriteMapRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlRewriteMapRepository>>()));

        services.Configure<RewriteMapEngineOptions>(
            configuration.GetSection("RewriteMapEngine"));

        services.AddSingleton<RewriteMapEngine>(sp =>
            new RewriteMapEngine(
                sp.GetRequiredService<IRewriteMapRepository>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RewriteMapEngineOptions>>(),
                sp.GetRequiredService<ILogger<RewriteMapEngine>>(),
                sp.GetRequiredService<IRewriteRuleHitRepository>()));

        services.AddSingleton<IRewriteMapCandidateRepository>(sp =>
            new SqlRewriteMapCandidateRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlRewriteMapCandidateRepository>>()));

        services.AddSingleton<RewriteMapPromotionService>(sp =>
            new RewriteMapPromotionService(
                connectionString,
                sp.GetRequiredService<IRewriteMapCandidateRepository>(),
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<RewriteMapPromotionService>>()));

        services.AddSingleton<IRewriteRuleHitRepository>(sp =>
            new SqlRewriteRuleHitRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlRewriteRuleHitRepository>>()));

        services.AddScoped<RewriteRuleHitBuffer>();

        // ------------------------------------------------------------
        // ✅ LUCENE SUGGESTIONS SERVICES
        // ------------------------------------------------------------
        services.AddSingleton<ILuceneSuggestionIndexRepository>(sp =>
            new SqlLuceneSuggestionIndexRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlLuceneSuggestionIndexRepository>>()));

        services.AddSingleton<ILuceneSuggestionEngine>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var indexPath = cfg["LuceneSuggestions:IndexPath"];
            if (string.IsNullOrWhiteSpace(indexPath))
                indexPath = "indexes/lucene/dictionary-rewrite-memory";

            return new LuceneSuggestionEngine(
                indexPath,
                sp.GetRequiredService<ILogger<LuceneSuggestionEngine>>());
        });

        services.AddSingleton<LuceneIndexBuilder>();

        // ------------------------------------------------------------
        // ✅ AI ENHANCEMENT SERVICES
        // ------------------------------------------------------------
        services.AddSingleton<IAiAnnotationRepository>(sp =>
            new SqlAiAnnotationRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlAiAnnotationRepository>>()));

        services.AddSingleton<IExampleAiEnhancementRepository>(sp =>
            new SqlExampleAiEnhancementRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlExampleAiEnhancementRepository>>()));

        services.AddScoped<AiEnhancementStep>();

        // ------------------------------------------------------------
        // ✅ IMPORT PIPELINE CORE
        // ------------------------------------------------------------
        services.Configure<ImportPipelineOptions>(configuration.GetSection("ImportPipeline"));
        services.AddScoped<ImportPipelineOrderResolver>();
        services.AddScoped<ImportPipelineRunner>();

        // ------------------------------------------------------------
        // ✅ PIPELINE STEP REGISTRATIONS (ORDER MATTERS)
        // ------------------------------------------------------------
        services.AddScoped<IImportPipelineStep, CanonicalizationPipelineStep>();
        services.AddScoped<IImportPipelineStep, ParsingPipelineStep>();
        services.AddScoped<IImportPipelineStep, LinguisticsPipelineStep>();
        services.AddScoped<IImportPipelineStep, GrammarCorrectionPipelineStep>();

        services.TryAddScoped<RuleBasedDefinitionEnhancementStep>();
        services.TryAddScoped<RuleBasedRewritePipelineStep>();
        services.AddScoped<IImportPipelineStep>(sp => sp.GetRequiredService<RuleBasedRewritePipelineStep>());

        services.TryAddScoped<LuceneMemorySuggestionsPipelineStep>();
        services.AddScoped<IImportPipelineStep>(sp => sp.GetRequiredService<LuceneMemorySuggestionsPipelineStep>());

        services.TryAddScoped<RuleBasedExampleRewritePipelineStep>();
        services.AddScoped<IImportPipelineStep>(sp => sp.GetRequiredService<RuleBasedExampleRewritePipelineStep>());

        services.AddScoped<IImportPipelineStep, AiEnhancementPipelineStep>();
        services.AddScoped<IImportPipelineStep, OrthographicSyllablesPipelineStep>();
        services.AddScoped<IImportPipelineStep, GraphBuildPipelineStep>();
        services.AddScoped<IImportPipelineStep, GraphValidationPipelineStep>();
        services.AddScoped<IImportPipelineStep, ConceptBuildPipelineStep>();
        services.AddScoped<IImportPipelineStep, ConceptMergePipelineStep>();
        services.AddScoped<IImportPipelineStep, IpaPipelineStep>();
        services.AddScoped<IImportPipelineStep, IpaSyllablesPipelineStep>();
        services.AddScoped<IImportPipelineStep, VerificationPipelineStep>();

        // ------------------------------------------------------------
        // ✅ CORE PROCESSING SERVICES
        // ------------------------------------------------------------
        services.AddScoped<DictionaryParsedDefinitionProcessor>(sp =>
            ActivatorUtilities.CreateInstance<DictionaryParsedDefinitionProcessor>(sp, connectionString));

        services.AddScoped<IParsedDefinitionProcessor>(sp =>
            sp.GetRequiredService<DictionaryParsedDefinitionProcessor>());

        services.AddTransient<DictionaryImporter.Sources.Generic.GenericExampleExtractor>();

        // ------------------------------------------------------------
        // ✅ QA SERVICES
        // ------------------------------------------------------------
        foreach (var qa in KnownQaChecks.CreateAll(connectionString))
            services.AddSingleton<IQaCheck>(qa);

        services.AddSingleton<QaRunner>();

        // ------------------------------------------------------------
        // ✅ WRITERS USING BATCHER
        // ------------------------------------------------------------
        services.AddTransient<IDictionaryEntryExampleWriter>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>();
            var batcher = sp.GetRequiredService<GenericSqlBatcher>();
            var exec = sp.GetRequiredService<ISqlStoredProcedureExecutor>();
            return new SqlDictionaryEntryExampleWriter(connectionString, batcher, exec, log);
        });

        services.AddTransient<SqlParsedDefinitionWriter>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>();
            var batcher = sp.GetRequiredService<GenericSqlBatcher>();
            return new SqlParsedDefinitionWriter(connectionString, batcher, log);
        });

        services.AddSingleton<IEntryEtymologyWriter>(sp =>
            new SqlDictionaryEntryEtymologyWriter(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

        services.AddSingleton<SqlDictionaryEntryVariantWriter>(sp =>
            new SqlDictionaryEntryVariantWriter(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlDictionaryEntryVariantWriter>>()));

        services.AddSingleton<IDictionaryEntryVariantWriter>(sp =>
            sp.GetRequiredService<SqlDictionaryEntryVariantWriter>());

        services.AddTransient<IDictionaryEntrySynonymWriter>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<SqlDictionaryEntrySynonymWriter>>();
            var batcher = sp.GetRequiredService<GenericSqlBatcher>();
            var exec = sp.GetRequiredService<ISqlStoredProcedureExecutor>();
            return new SqlDictionaryEntrySynonymWriter(connectionString, log, batcher, exec);
        });

        services.AddSingleton<IDictionaryImportControl>(
            sp => new DictionaryImportControl(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryImportControl>>()));

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
                sp.GetRequiredService<ILanguageDetectionService>(),
                sp.GetRequiredService<INonEnglishTextStorage>(),
                sp.GetRequiredService<IOcrArtifactNormalizer>(),
                sp.GetRequiredService<IDefinitionNormalizer>(),
                sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>(),
                sp.GetService<IOptions<BatchProcessingSettings>>(),   // optional
                sp.GetService<IBatchProcessedDataCollector>()          // optional
            );
        });

        // ------------------------------------------------------------
        // ✅ IMPORT ORCHESTRATOR WITH ALL DEPENDENCIES
        // ------------------------------------------------------------
        services.AddScoped<ImportOrchestrator>(sp =>
        {
            // Resolve all required dependencies
            var validatorFactory = sp.GetRequiredService<Func<IDictionaryEntryValidator>>();
            var mergeFactory = sp.GetRequiredService<Func<IDataMergeExecutor>>();
            var engineRegistry = sp.GetRequiredService<IImportEngineRegistry>();
            var canonicalResolver = sp.GetRequiredService<ICanonicalWordResolver>();
            var parsedDefinitionProcessor = sp.GetRequiredService<IParsedDefinitionProcessor>();
            var linguisticEnricher = sp.GetRequiredService<DictionaryEntryLinguisticEnricher>();
            var orthographicSyllableEnricher = sp.GetRequiredService<CanonicalWordOrthographicSyllableEnricher>();
            var graphNodeBuilder = sp.GetRequiredService<DictionaryGraphNodeBuilder>();
            var graphBuilder = sp.GetRequiredService<DictionaryGraphBuilder>();
            var graphValidator = sp.GetRequiredService<DictionaryGraphValidator>();
            var conceptBuilder = sp.GetRequiredService<DictionaryConceptBuilder>();
            var conceptMerger = sp.GetRequiredService<DictionaryConceptMerger>();
            var conceptConfidenceCalculator = sp.GetRequiredService<DictionaryConceptConfidenceCalculator>();
            var graphRankCalculator = sp.GetRequiredService<DictionaryGraphRankCalculator>();
            var postMergeVerifier = sp.GetRequiredService<IPostMergeVerifier>();
            var ipaEnricher = sp.GetRequiredService<CanonicalWordIpaEnricher>();
            var syllableEnricher = sp.GetRequiredService<CanonicalWordSyllableEnricher>();
            var ipaVerificationReporter = sp.GetRequiredService<IpaVerificationReporter>();
            var ipaSources = sp.GetRequiredService<IReadOnlyList<IpaSourceConfig>>();
            var aiEnhancementStep = sp.GetRequiredService<AiEnhancementStep>();
            var pipelineRunner = sp.GetRequiredService<ImportPipelineRunner>();
            var pipelineOrderResolver = sp.GetRequiredService<ImportPipelineOrderResolver>();
            services.Configure<BatchProcessingSettings>(configuration.GetSection("BatchProcessing"));
            services.Configure<ParallelProcessingSettings>(configuration.GetSection("ParallelProcessing"));

            var concurrencyManager = sp.GetRequiredService<ImportConcurrencyManager>(); // NEW
            var logger = sp.GetRequiredService<ILogger<ImportOrchestrator>>();
            var qaRunner = sp.GetRequiredService<QaRunner>();

            return new ImportOrchestrator(
                validatorFactory,
                mergeFactory,
                engineRegistry,
                canonicalResolver,
                parsedDefinitionProcessor,
                linguisticEnricher,
                orthographicSyllableEnricher,
                graphNodeBuilder,
                graphBuilder,
                graphValidator,
                conceptBuilder,
                conceptMerger,
                conceptConfidenceCalculator,
                graphRankCalculator,
                postMergeVerifier,
                ipaEnricher,
                syllableEnricher,
                ipaVerificationReporter,
                ipaSources,
                aiEnhancementStep,
                pipelineRunner,
                pipelineOrderResolver,
                concurrencyManager, // NEW
                logger,
                qaRunner);
        });

        services.AddHostedService<StartupCleanupHostedService>();

        // ------------------------------------------------------------
        // ✅ REGISTER SOURCE MODULES
        // ------------------------------------------------------------
        var sourceModules = SourceRegistry.CreateSources().ToList();
        foreach (var module in sourceModules)
        {
            module.RegisterServices(services, configuration);
        }
    }

    public static PipelineMode ResolvePipelineMode(IConfiguration configuration)
    {
        return configuration["Pipeline:Mode"] == "ImportOnly"
            ? PipelineMode.ImportOnly
            : PipelineMode.Full;
    }
}