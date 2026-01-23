using DictionaryImporter.Core.Text;
using DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry;
using DictionaryImporter.Sources.Collins.Extractor;
using DictionaryImporter.Sources.Common.Parsing;
using DictionaryImporter.Sources.EnglishChinese.Extractor;
using DictionaryImporter.Sources.Generic;
using DictionaryImporter.Sources.Gutenberg.Extractor;
using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Bootstrap.Extensions
{
    public static class ParsingRegistrationExtensions
    {
        public static IServiceCollection AddParsing(this IServiceCollection services, string connectionString)
        {
            services.AddSingleton<IDictionaryDefinitionParser, OxfordDefinitionParser>();

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

            services.AddSingleton<IDictionaryEntrySynonymWriter>(sp =>
                new SqlDictionaryEntrySynonymWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntrySynonymWriter>>(),
                    sp.GetRequiredService<GenericSqlBatcher>()));

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

            services.AddSingleton<IDictionaryEntryCrossReferenceWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryEntryCrossReferenceWriter>());

            services.AddSingleton<IDictionaryEntryAliasWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryAliasWriter>());

            services.AddSingleton<IEntryEtymologyWriter>(sp =>
                new SqlDictionaryEntryEtymologyWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

            services.AddSingleton<IDictionaryTextFormatter>(sp =>
                new DefaultDictionaryTextFormatter(
                    sp.GetRequiredService<ILogger<DefaultDictionaryTextFormatter>>()));

            services.AddSingleton<IGrammarEnrichedTextService>(sp =>
                new GrammarEnrichedTextService(
                    sp.GetRequiredService<ILogger<GrammarEnrichedTextService>>()));

            // -------------------------------------------
            // Example Extractors ✅ FIXED
            // -------------------------------------------
            services.AddSingleton<IExampleExtractor, GutenbergExampleExtractor>();

            // ✅ REQUIRED: ExampleExtractorRegistry needs this
            services.AddSingleton<GenericExampleExtractor>();

            services.AddSingleton<IExampleExtractorRegistry>(sp =>
                new ExampleExtractorRegistry(
                    sp.GetServices<IExampleExtractor>(),
                    sp.GetRequiredService<GenericExampleExtractor>(),
                    sp.GetRequiredService<ILogger<ExampleExtractorRegistry>>()));

            // -------------------------------------------
            // Synonym Extractors
            // -------------------------------------------
            services.AddSingleton<ISynonymExtractor, EnglishChineseSynonymExtractor>();
            services.AddSingleton<ISynonymExtractor, CollinsSynonymExtractor>();
            services.AddSingleton<ISynonymExtractor, GutenbergSynonymExtractor>();

            services.AddSingleton<GenericSynonymExtractor>();

            services.AddSingleton<ISynonymExtractorRegistry>(sp =>
                new SynonymExtractorRegistry(
                    sp.GetServices<ISynonymExtractor>(),
                    sp.GetRequiredService<GenericSynonymExtractor>(),
                    sp.GetRequiredService<ILogger<SynonymExtractorRegistry>>()));

            // -------------------------------------------
            // Etymology Extractors
            // -------------------------------------------
            services.AddSingleton<IEtymologyExtractor, EnglishChineseEtymologyExtractor>();
            services.AddSingleton<IEtymologyExtractor, GutenbergEtymologyExtractor>();

            services.AddSingleton<GenericEtymologyExtractor>();

            services.AddSingleton<IEtymologyExtractorRegistry>(sp =>
                new EtymologyExtractorRegistry(
                    sp.GetServices<IEtymologyExtractor>(),
                    sp.GetRequiredService<GenericEtymologyExtractor>(),
                    sp.GetRequiredService<ILogger<EtymologyExtractorRegistry>>()));

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
                    sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()
                );
            });

            return services;
        }
    }
}
