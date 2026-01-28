using DictionaryImporter.Bootstrap.Extensions;
using DictionaryImporter.Core.Jobs;
using DictionaryImporter.Gateway.Ai.Bootstrap;

namespace DictionaryImporter.Bootstrap;

public static class BootstrapInfrastructure
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DictionaryImporter")
                               ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

        BootstrapLogging.Register(services);

        // ✅ Register RuleBased rewrite job (Option 2)
        services.Configure<RuleBasedRewriteJobOptions>(
            configuration.GetSection("RuleBasedRewriteJob"));
        services.AddScoped<RuleBasedRewriteJob>();

        // Add SQL batching
        services.AddSqlBatching(connectionString);

        // Add non-English text services
        services.AddNonEnglishTextServices(connectionString);

        // ✅ CRITICAL FIX: Register Grammar BEFORE Parsing
        services
            .AddIpaConfiguration(configuration)
            .AddPersistenceWithoutSynonymWriter(connectionString)
            .AddCanonical(connectionString)
            .AddValidation(connectionString)
            .AddLinguistics()
            .AddGrammar(configuration)          // ✅ Grammar + DictionaryRewriteCorrector are registered here
            .AddParsing(connectionString)
            .AddGraph(connectionString)
            .AddConcepts(connectionString)
            .AddIpa(connectionString)
            .AddDistributedMemoryCache()
            .AddDictionaryImporterAiGateway(configuration);

        // ✅ ADD PARALLEL PROCESSING REGISTRATION
        RegisterParallelProcessingServices(services, configuration, connectionString);
    }

    private static void RegisterParallelProcessingServices(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        // Register ImportConcurrencyManager
        services.AddSingleton<ImportConcurrencyManager>();

        // Register the ImportOrchestrator with the new interface
        services.AddScoped<IImportOrchestrator>(sp =>
        {
            // Get all required services
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
            var concurrencyManager = sp.GetRequiredService<ImportConcurrencyManager>();
            var logger = sp.GetRequiredService<ILogger<ImportOrchestrator>>();
            var qaRunner = sp.GetRequiredService<QaRunner>();

            // Create the orchestrator
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
                concurrencyManager,
                logger,
                qaRunner);
        });
    }
}