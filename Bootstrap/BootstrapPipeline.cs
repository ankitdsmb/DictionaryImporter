using DictionaryImporter.Infrastructure.Grammar;
using DictionaryImporter.Infrastructure.Merge;
using DictionaryImporter.Infrastructure.OneTimeTasks;
using DictionaryImporter.Infrastructure.Parsing;
using DictionaryImporter.Infrastructure.Parsing.EtymologyExtractor;
using DictionaryImporter.Infrastructure.Parsing.ExampleExtractor;
using DictionaryImporter.Infrastructure.Parsing.SynonymExtractor;
using DictionaryImporter.Infrastructure.PostProcessing;
using DictionaryImporter.Infrastructure.PostProcessing.Verification;
using DictionaryImporter.Infrastructure.Qa;
using DictionaryImporter.Infrastructure.Verification;
using DictionaryImporter.Sources.Century21.Models;
using DictionaryImporter.Sources.Kaikki.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap;

public static class BootstrapPipeline
{
    public static void Register(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("DictionaryImporter")
            ?? throw new InvalidOperationException(
                "Connection string 'DictionaryImporter' not configured");
        // Kaikki import engine
        services.AddSingleton<ImportEngineFactory<KaikkiRawEntry>>();
        services.AddSingleton<Func<ImportEngineFactory<KaikkiRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<KaikkiRawEntry>>());

        // =====================================================
        // IMPORT ENGINES
        // =====================================================
        services.AddSingleton<ImportEngineFactory<GutenbergRawEntry>>();
        services.AddSingleton<ImportEngineFactory<StructuredJsonRawEntry>>();
        services.AddSingleton<ImportEngineFactory<EnglishChineseRawEntry>>();
        services.AddSingleton<ImportEngineFactory<CollinsRawEntry>>();
        services.AddSingleton<ImportEngineFactory<OxfordRawEntry>>();
        services.AddSingleton<ImportEngineFactory<Century21RawEntry>>();

        services.AddSingleton<Func<IDictionaryEntryValidator>>(sp =>
            () => sp.GetRequiredService<IDictionaryEntryValidator>());

        services.AddSingleton<Func<IDataMergeExecutor>>(sp =>
            () => sp.GetRequiredService<IDataMergeExecutor>());

        services.AddSingleton<Func<ImportEngineFactory<GutenbergRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<GutenbergRawEntry>>());
        services.AddSingleton<Func<ImportEngineFactory<StructuredJsonRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<StructuredJsonRawEntry>>());
        services.AddSingleton<Func<ImportEngineFactory<EnglishChineseRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<EnglishChineseRawEntry>>());
        services.AddSingleton<Func<ImportEngineFactory<CollinsRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<CollinsRawEntry>>());
        services.AddSingleton<Func<ImportEngineFactory<OxfordRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<OxfordRawEntry>>());
        services.AddSingleton<Func<ImportEngineFactory<Century21RawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<Century21RawEntry>>());

        services.AddSingleton<IImportEngineRegistry, ImportEngineRegistry>();

        // Oxford-specific extractors
        services.AddSingleton<IEtymologyExtractor, OxfordEtymologyExtractor>();
        services.AddSingleton<IExampleExtractor, OxfordExampleExtractor>();
        services.AddSingleton<ISynonymExtractor, OxfordSynonymExtractor>();

        // =====================================================
        // POST-PROCESSING / ENRICHMENT
        // =====================================================
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

        // =====================================================
        // VERIFICATION
        // =====================================================

        services.AddSingleton<IDataMergeExecutor>(sp =>
            new SqlDictionaryEntryMergeExecutor(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

        services.AddSingleton<IpaVerificationReporter>(sp =>
            new IpaVerificationReporter(
                connectionString,
                sp.GetRequiredService<ILogger<IpaVerificationReporter>>()));

        // =====================================================
        // ORTHOGRAPHIC SYLLABLES (NEW)
        // =====================================================

        // Rule engine (pure, stateless)
        services.AddSingleton<OrthographicSyllableRuleResolver>();

        // Enricher (needs connection string + rules)
        services.AddSingleton<CanonicalWordOrthographicSyllableEnricher>(sp =>
            new CanonicalWordOrthographicSyllableEnricher(
                connectionString,
                sp.GetRequiredService<ILogger<CanonicalWordOrthographicSyllableEnricher>>()));

        // =====================================================
        // ONE-TIME DATABASE TASKS (MANUAL EXECUTION ONLY)
        // =====================================================
        services.AddSingleton<IOneTimeDatabaseTask>(
            new EditorialIpaMigrationTask(connectionString));

        services.AddSingleton<IOneTimeDatabaseTask>(
            new PromoteIpaFromNotesTask(connectionString));

        services.AddSingleton<OneTimeTaskRunner>();

        // Add to BootstrapPipeline.Register method
        services.AddSingleton<ImportEngineFactory<OxfordRawEntry>>();
        services.AddSingleton<Func<ImportEngineFactory<OxfordRawEntry>>>(sp =>
            () => sp.GetRequiredService<ImportEngineFactory<OxfordRawEntry>>());

        // =====================================================
        // QA (READ-ONLY VERIFICATION)
        // =====================================================
        foreach (var qa in KnownQaChecks.CreateAll(connectionString)) services.AddSingleton<IQaCheck>(qa);

        services.AddSingleton<QaRunner>();

        // =====================================================
        // ORCHESTRATOR
        // =====================================================
        services.AddSingleton<ImportOrchestrator>(sp =>
        {
            // Ensure all required services are available
            return new ImportOrchestrator(
                sp.GetRequiredService<Func<IDictionaryEntryValidator>>(),
                sp.GetRequiredService<Func<IDataMergeExecutor>>(),
                sp.GetRequiredService<IImportEngineRegistry>(),
                sp.GetRequiredService<ICanonicalWordResolver>(),
                sp.GetRequiredService<DictionaryParsedDefinitionProcessor>(),
                sp.GetRequiredService<DictionaryEntryLinguisticEnricher>(),
                sp.GetRequiredService<CanonicalWordOrthographicSyllableEnricher>(),
                sp.GetRequiredService<DictionaryGraphNodeBuilder>(),
                sp.GetRequiredService<DictionaryGraphBuilder>(),
                sp.GetRequiredService<DictionaryGraphValidator>(),
                sp.GetRequiredService<DictionaryConceptBuilder>(),
                sp.GetRequiredService<DictionaryConceptMerger>(),
                sp.GetRequiredService<DictionaryConceptConfidenceCalculator>(),
                sp.GetRequiredService<DictionaryGraphRankCalculator>(),
                sp.GetRequiredService<IPostMergeVerifier>(),
                sp.GetRequiredService<CanonicalWordIpaEnricher>(),
                sp.GetRequiredService<CanonicalWordSyllableEnricher>(),
                sp.GetRequiredService<IpaVerificationReporter>(),
                sp.GetRequiredService<IReadOnlyList<IpaSourceConfig>>(),
                // ADD GrammarCorrectionStep here - position 20
                sp.GetRequiredService<GrammarCorrectionStep>(),  // ← THIS IS POSITION 20
                                                                 // Continue with remaining parameters
                sp.GetRequiredService<ILogger<ImportOrchestrator>>(),  // ← THIS IS POSITION 21
                sp.GetRequiredService<QaRunner>()  // ← THIS IS POSITION 22
            );
        });
    }

    public static PipelineMode ResolvePipelineMode(IConfiguration configuration)
    {
        return configuration["Pipeline:Mode"] == "ImportOnly"
            ? PipelineMode.ImportOnly
            : PipelineMode.Full;
    }
}