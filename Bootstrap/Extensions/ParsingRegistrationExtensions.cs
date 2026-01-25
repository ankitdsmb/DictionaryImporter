using DictionaryImporter.Core.Persistence;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Infrastructure.Parsing; // Needed for DictionaryParsedDefinitionProcessor
using DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Sources.Collins.Extractor;
using DictionaryImporter.Sources.EnglishChinese.Extractor;
using DictionaryImporter.Sources.Generic;
using DictionaryImporter.Sources.Gutenberg.Extractor;
using DictionaryImporter.Sources.Gutenberg.Parsing;
using DictionaryImporter.Sources.Oxford.Parsing;
using DictionaryImporter.Sources.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Extensions
{
    public static class ParsingRegistrationExtensions
    {
        public static IServiceCollection AddParsing(this IServiceCollection services, string connectionString)
        {
            // ✅ REQUIRED: Stored Procedure Executor
            // NOTE: If already registered elsewhere, multiple registrations are not fatal,
            // but recommended is to keep it registered only once globally.
            services.AddSingleton<ISqlStoredProcedureExecutor>(_ =>
                new SqlStoredProcedureExecutor(connectionString));

            // 1. Core Text Processing Services (New dependencies)
            services.AddSingleton<IOcrArtifactNormalizer, OcrArtifactNormalizer>();
            services.AddSingleton<IDefinitionNormalizer, DefinitionNormalizer>();

            // 2. Text Formatter (Uses OCR & Definition normalizers)
            services.AddSingleton<IDictionaryTextFormatter, DictionaryTextFormatter>();

            // 3. Parsers
            services.AddSingleton<IDictionaryDefinitionParser, OxfordDefinitionParser>();

            // 4. Writers (using connectionString)
            services.AddTransient<IDictionaryEntryExampleWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                var exec = sp.GetRequiredService<ISqlStoredProcedureExecutor>();
                return new SqlDictionaryEntryExampleWriter(connectionString, batcher,exec, logger);
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
                    sp.GetRequiredService<ISqlStoredProcedureExecutor>()));

            services.AddSingleton<SqlDictionaryEntryCrossReferenceWriter>(sp =>
                new SqlDictionaryEntryCrossReferenceWriter(
                    sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>()));

            // ✅ FIX: SqlDictionaryAliasWriter now requires ISqlStoredProcedureExecutor
            services.AddSingleton<SqlDictionaryAliasWriter>(sp =>
                new SqlDictionaryAliasWriter(
                    sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                    sp.GetRequiredService<ILogger<SqlDictionaryAliasWriter>>()));

            services.AddSingleton<SqlDictionaryEntryVariantWriter>(sp =>
                new SqlDictionaryEntryVariantWriter(
                    sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryVariantWriter>>()));

            // Forwarding implementations
            services.AddSingleton<IDictionaryEntryVariantWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryEntryVariantWriter>());

            services.AddSingleton<IDictionaryEntryCrossReferenceWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryEntryCrossReferenceWriter>());

            services.AddSingleton<IDictionaryEntryAliasWriter>(sp =>
                sp.GetRequiredService<SqlDictionaryAliasWriter>());

            services.AddSingleton<IEntryEtymologyWriter>(sp =>
                new SqlDictionaryEntryEtymologyWriter(
                    sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

            // 5. Text Services
            services.AddSingleton<IGrammarEnrichedTextService>(sp =>
                new GrammarEnrichedTextService(
                    sp.GetRequiredService<ILogger<GrammarEnrichedTextService>>()));

            // 6. Extractors
            // Examples
            services.AddSingleton<IExampleExtractor, GutenbergExampleExtractor>();
            services.AddSingleton<GenericExampleExtractor>();

            services.AddSingleton<IExampleExtractorRegistry>(sp =>
                new ExampleExtractorRegistry(
                    sp.GetServices<IExampleExtractor>(),
                    sp.GetRequiredService<GenericExampleExtractor>(),
                    sp.GetRequiredService<ILogger<ExampleExtractorRegistry>>()));

            // Synonyms
            services.AddSingleton<ISynonymExtractor, EnglishChineseSynonymExtractor>();
            services.AddSingleton<ISynonymExtractor, CollinsSynonymExtractor>();
            services.AddSingleton<ISynonymExtractor, GutenbergSynonymExtractor>();
            services.AddSingleton<GenericSynonymExtractor>();

            services.AddSingleton<ISynonymExtractorRegistry>(sp =>
                new SynonymExtractorRegistry(
                    sp.GetServices<ISynonymExtractor>(),
                    sp.GetRequiredService<GenericSynonymExtractor>(),
                    sp.GetRequiredService<ILogger<SynonymExtractorRegistry>>()));

            // Etymology
            services.AddSingleton<IEtymologyExtractor, EnglishChineseEtymologyExtractor>();
            services.AddSingleton<IEtymologyExtractor, GutenbergEtymologyExtractor>();
            services.AddSingleton<GenericEtymologyExtractor>();

            services.AddSingleton<IEtymologyExtractorRegistry>(sp =>
                new EtymologyExtractorRegistry(
                    sp.GetServices<IEtymologyExtractor>(),
                    sp.GetRequiredService<GenericEtymologyExtractor>(),
                    sp.GetRequiredService<ILogger<EtymologyExtractorRegistry>>()));

            // 7. Main Processor Registration (Updated with new dependencies)
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
                    sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()
                );
            });

            return services;
        }
    }
}
