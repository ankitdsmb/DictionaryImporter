using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Canonical;
using DictionaryImporter.Core.Linguistics;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Infrastructure;
using DictionaryImporter.Infrastructure.Canonical;
using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Infrastructure.Linguistics;
using DictionaryImporter.Infrastructure.Merge;
using DictionaryImporter.Infrastructure.Parsing;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Infrastructure.PostProcessing;
using DictionaryImporter.Infrastructure.PostProcessing.Enrichment;
using DictionaryImporter.Infrastructure.PostProcessing.Verification;
using DictionaryImporter.Infrastructure.Verification;
using DictionaryImporter.Sources.Gutenberg.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap
{
    public static class BootstrapInfrastructure
    {
        public static void Register(
            IServiceCollection services,
            IConfiguration configuration)
        {
            var connectionString =
                configuration.GetConnectionString("DictionaryImporter")
                ?? throw new InvalidOperationException(
                    "Connection string 'DictionaryImporter' not configured");

            services.AddSingleton(
                configuration
                    .GetSection("IPA:Sources")
                    .Get<IReadOnlyList<IpaSourceConfig>>()
                ?? Array.Empty<IpaSourceConfig>());

            // Persistence & merge
            services.AddSingleton<IStagingLoader>(sp =>
                new SqlDictionaryEntryStagingLoader(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryStagingLoader>>()));

            services.AddSingleton<IDataLoader, StagingDataLoaderAdapter>();
            services.AddSingleton<IEntryEtymologyWriter>(
                _ => new SqlDictionaryEntryEtymologyWriter(connectionString));

            services.AddSingleton<IDataMergeExecutor>(sp =>
                new SqlDictionaryEntryMergeExecutor(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

            services.AddSingleton<IPostMergeVerifier>(sp =>
                new SqlPostMergeVerifier(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlPostMergeVerifier>>()));

            // Canonical
            services.AddSingleton<ICanonicalWordResolver>(sp =>
                new SqlCanonicalWordResolver(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlCanonicalWordResolver>>()));

            // Validation
            services.AddTransient<IDictionaryEntryValidator,
                DefaultDictionaryEntryValidator>();

            services.AddSingleton<DictionaryEntryLinguisticEnricher>(sp =>
                new DictionaryEntryLinguisticEnricher(
                    connectionString,
                    sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                    sp.GetRequiredService<ILogger<DictionaryEntryLinguisticEnricher>>()));

            // Linguistics
            services.AddSingleton<IPartOfSpeechInfererV2,
                ParsedDefinitionPartOfSpeechInfererV2>();

            // Parsing
            services.AddSingleton<IDictionaryDefinitionParser,
                WebsterSubEntryParser>();

            services.AddSingleton<SqlParsedDefinitionWriter>(sp => new SqlParsedDefinitionWriter(connectionString, sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>()));
            services.AddSingleton(_ => new SqlDictionaryAliasWriter(connectionString));
            services.AddSingleton(_ => new SqlDictionaryEntrySynonymWriter(connectionString));
            services.AddSingleton<SqlDictionaryEntryCrossReferenceWriter>(sp => new SqlDictionaryEntryCrossReferenceWriter(connectionString, sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>()));
            services.AddSingleton(_ => new SqlDictionaryEntryVariantWriter(connectionString));

            services.AddSingleton<DictionaryParsedDefinitionProcessor>(sp =>
                new DictionaryParsedDefinitionProcessor(
                    connectionString,
                    sp.GetRequiredService<IDictionaryDefinitionParser>(),
                    sp.GetRequiredService<SqlParsedDefinitionWriter>(),
                    sp.GetRequiredService<SqlDictionaryEntryCrossReferenceWriter>(),
                    sp.GetRequiredService<SqlDictionaryAliasWriter>(),
                    sp.GetRequiredService<IEntryEtymologyWriter>(),
                    sp.GetRequiredService<SqlDictionaryEntryVariantWriter>(),
                    sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()));

            // Graph
            services.AddSingleton<DictionaryGraphNodeBuilder>(sp => new DictionaryGraphNodeBuilder(connectionString, sp.GetRequiredService<ILogger<DictionaryGraphNodeBuilder>>()));
            services.AddSingleton<DictionaryGraphBuilder>(sp => new DictionaryGraphBuilder(connectionString, sp.GetRequiredService<ILogger<DictionaryGraphBuilder>>()));
            services.AddSingleton<DictionaryGraphValidator>(sp =>
                new DictionaryGraphValidator(
                    connectionString,
                    sp.GetRequiredService<ILogger<DictionaryGraphValidator>>()));

            // Concepts & ranking
            services.AddSingleton<DictionaryConceptBuilder>(sp =>
                new DictionaryConceptBuilder(
                    connectionString,
                    sp.GetRequiredService<ILogger<DictionaryConceptBuilder>>()
                ));

            services.AddSingleton<DictionaryConceptMerger>(sp => new DictionaryConceptMerger(connectionString, sp.GetRequiredService<ILogger<DictionaryConceptMerger>>()));
            services.AddSingleton<DictionaryConceptConfidenceCalculator>(sp => new DictionaryConceptConfidenceCalculator(connectionString, sp.GetRequiredService<ILogger<DictionaryConceptConfidenceCalculator>>()));
            services.AddSingleton<DictionaryGraphRankCalculator>(sp => new DictionaryGraphRankCalculator(connectionString, sp.GetRequiredService<ILogger<DictionaryGraphRankCalculator>>()));

            // IPA
            services.AddSingleton(
                _ => new SqlCanonicalWordPronunciationWriter(connectionString));

            services.AddSingleton<CanonicalWordIpaEnricher>(sp =>
                new CanonicalWordIpaEnricher(
                    connectionString,
                    sp.GetRequiredService<SqlCanonicalWordPronunciationWriter>(),
                    sp.GetRequiredService<ILogger<CanonicalWordIpaEnricher>>()));

            services.AddSingleton<IpaVerificationReporter>(sp =>
                new IpaVerificationReporter(
                    connectionString,
                    sp.GetRequiredService<ILogger<IpaVerificationReporter>>()));
        }
    }
}
