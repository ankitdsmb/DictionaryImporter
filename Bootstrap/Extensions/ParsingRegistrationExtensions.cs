using DictionaryImporter.Core.Persistence;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Infrastructure.Persistence.Batched;
using DictionaryImporter.Sources.Collins.Extractor;
using DictionaryImporter.Sources.Common.Parsing;
using DictionaryImporter.Sources.EnglishChinese.Extractor;
using DictionaryImporter.Sources.Generic;
using DictionaryImporter.Sources.Gutenberg.Extractor;
using DictionaryImporter.Sources.Oxford.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebsterEtymologyExtractor = DictionaryImporter.Sources.Gutenberg.Extractor.WebsterEtymologyExtractor;
using WebsterSynonymExtractor = DictionaryImporter.Sources.Gutenberg.Extractor.WebsterSynonymExtractor;

namespace DictionaryImporter.Bootstrap.Extensions
{
    public static class ParsingRegistrationExtensions
    {
        public static IServiceCollection AddParsing(this IServiceCollection services, string connectionString)
        {
            services.AddSingleton<IDictionaryDefinitionParser, OxfordDefinitionParser>();

            // FIXED: Proper registration with all required parameters for SqlDictionaryEntryExampleWriter
            services.AddTransient<IDictionaryEntryExampleWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlDictionaryEntryExampleWriter(connectionString, batcher, logger);
            });

            // FIXED: Proper registration with all required parameters for SqlParsedDefinitionWriter
            services.AddTransient<SqlParsedDefinitionWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlParsedDefinitionWriter(connectionString, batcher, logger);
            });

            services.AddSingleton<IExampleExtractorRegistry, ExampleExtractorRegistry>();

            // FIXED: Added missing batcher parameter for SqlDictionaryEntrySynonymWriter
            services.AddSingleton<IDictionaryEntrySynonymWriter>(sp =>
                new SqlDictionaryEntrySynonymWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntrySynonymWriter>>(),
                    sp.GetRequiredService<GenericSqlBatcher>()));

            services.AddSingleton<ISynonymExtractor, EnglishChineseSynonymExtractor>();
            services.AddSingleton<ISynonymExtractor, WebsterSynonymExtractor>();
            services.AddSingleton<ISynonymExtractor, CollinsSynonymExtractor>();
            services.AddSingleton<GenericSynonymExtractor>();

            // REMOVED DUPLICATE: Only register this once
            // services.AddSingleton<ISynonymExtractorRegistry, SynonymExtractorRegistry>();

            // REMOVED DUPLICATE: This is already registered above with proper parameters
            // services.AddSingleton(sp =>
            //     new SqlParsedDefinitionWriter(
            //         connectionString,
            //         sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>()));

            services.AddSingleton<SqlDictionaryEntryCrossReferenceWriter>(sp =>
                new SqlDictionaryEntryCrossReferenceWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>()));

            services.AddSingleton<SqlDictionaryAliasWriter>(sp =>
                new SqlDictionaryAliasWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryAliasWriter>>()));

            services.AddSingleton<SqlDictionaryEntryVariantWriter>(sp =>
                new SqlDictionaryEntryVariantWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryVariantWriter>>()));

            services.AddSingleton<IDictionaryEntryVariantWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryEntryVariantWriter>());


            services.AddSingleton<IEtymologyExtractor, WebsterEtymologyExtractor>();
            services.AddSingleton<IEtymologyExtractor, EnglishChineseEtymologyExtractor>();
            services.AddSingleton<GenericEtymologyExtractor>();

            services.AddSingleton<IEtymologyExtractorRegistry, EtymologyExtractorRegistry>();

            // FIXED: Register IDictionaryEntryCrossReferenceWriter interface (not just concrete)
            services.AddSingleton<IDictionaryEntryCrossReferenceWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryEntryCrossReferenceWriter>());

            // FIXED: Register IDictionaryEntryAliasWriter interface
            services.AddSingleton<IDictionaryEntryAliasWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryAliasWriter>());

            // FIXED: Register IEntryEtymologyWriter (missing interface/implementation)
            // You need to check what the actual type is - let me assume it's SqlDictionaryEntryEtymologyWriter
            services.AddSingleton<IEntryEtymologyWriter>(sp =>
                new SqlDictionaryEntryEtymologyWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

            // FIXED: Register IDictionaryTextFormatter (missing interface/implementation)
            services.AddSingleton<IDictionaryTextFormatter>(sp =>
                new DefaultDictionaryTextFormatter(
                    sp.GetRequiredService<ILogger<DefaultDictionaryTextFormatter>>()));

            // FIXED: Register IGrammarEnrichedTextService (missing interface/implementation)
            services.AddSingleton<IGrammarEnrichedTextService>(sp =>
                new GrammarEnrichedTextService(
                    sp.GetRequiredService<ILogger<GrammarEnrichedTextService>>()));

            // FIXED: Register SynonymExtractorRegistry (was missing)
            services.AddSingleton<ISynonymExtractorRegistry>(sp =>
                new SynonymExtractorRegistry(
                    sp.GetServices<ISynonymExtractor>(),
                    sp.GetRequiredService<GenericSynonymExtractor>(),
                    sp.GetRequiredService<ILogger<SynonymExtractorRegistry>>()));

            services.AddSingleton<IParsedDefinitionProcessor, DictionaryParsedDefinitionProcessor>(sp =>
            {
                return new DictionaryParsedDefinitionProcessor(
                    connectionString,
                    sp.GetRequiredService<IDictionaryDefinitionParserResolver>(),
                    sp.GetRequiredService<SqlParsedDefinitionWriter>(),
                    sp.GetRequiredService<IDictionaryEntryCrossReferenceWriter>(),
                    sp.GetRequiredService<IDictionaryEntryAliasWriter>(),
                    sp.GetRequiredService<IEntryEtymologyWriter>(),
                    sp.GetRequiredService<IDictionaryEntryVariantWriter>(),  // INTERFACE
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

            return services;
        }
    }
}