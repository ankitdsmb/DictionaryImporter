using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Linguistics;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Infrastructure.Merge;
using DictionaryImporter.Infrastructure.OneTimeTasks;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Infrastructure.PostProcessing;
using DictionaryImporter.Infrastructure.PostProcessing.Enrichment;
using DictionaryImporter.Infrastructure.PostProcessing.Verification;
using DictionaryImporter.Infrastructure.Qa;
using DictionaryImporter.Orchestration;
using DictionaryImporter.Sources.EnglishChinese;
using DictionaryImporter.Sources.Gutenberg;
using DictionaryImporter.Sources.StructuredJson;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap
{
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

            // =====================================================
            // IMPORT ENGINES
            // =====================================================
            services.AddSingleton<ImportEngineFactory<GutenbergRawEntry>>();
            services.AddSingleton<ImportEngineFactory<StructuredJsonRawEntry>>();
            services.AddSingleton<ImportEngineFactory<EnglishChineseRawEntry>>();

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

            services.AddSingleton<IImportEngineRegistry, ImportEngineRegistry>();

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

            // =====================================================
            // QA (READ-ONLY VERIFICATION)
            // =====================================================
            foreach (var qa in KnownQaChecks.CreateAll(connectionString))
            {
                services.AddSingleton<IQaCheck>(qa);
            }

            services.AddSingleton<QaRunner>();

            // =====================================================
            // ORCHESTRATOR
            // =====================================================
            services.AddSingleton<ImportOrchestrator>();
        }

        public static PipelineMode ResolvePipelineMode(IConfiguration configuration)
        {
            return configuration["Pipeline:Mode"] == "ImportOnly"
                ? PipelineMode.ImportOnly
                : PipelineMode.Full;
        }
    }
}
