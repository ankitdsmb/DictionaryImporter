// Update ParsingRegistrationExtensions.cs

using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Core.Persistence;
using DictionaryImporter.Infrastructure.Parsing;
using DictionaryImporter.Infrastructure.Parsing.EtymologyExtractor;
using DictionaryImporter.Infrastructure.Parsing.ExampleExtractor;
using DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry;
using DictionaryImporter.Infrastructure.Parsing.SynonymExtractor;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Sources.Gutenberg.Parsing;
using Microsoft.Extensions.DependencyInjection;
using WebsterEtymologyExtractor =
    DictionaryImporter.Infrastructure.Parsing.EtymologyExtractor.WebsterEtymologyExtractor;
using WebsterSynonymExtractor = DictionaryImporter.Infrastructure.Parsing.SynonymExtractor.WebsterSynonymExtractor;

namespace DictionaryImporter.Bootstrap.Exensions;

public static class ParsingRegistrationExtensions
{
    public static IServiceCollection AddParsing(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDictionaryDefinitionParser, OxfordDefinitionParser>();

        // Register example writers
        services.AddSingleton<IDictionaryEntryExampleWriter>(sp =>
            new SqlDictionaryEntryExampleWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>()));

        // Register example extractors
        services.AddSingleton<IExampleExtractor, WebsterExampleExtractor>();
        services.AddSingleton<IExampleExtractor, EnglishChineseExampleExtractor>();
        services.AddSingleton<GenericExampleExtractor>(); // Special registration for generic

        // Register example extractor registry
        services.AddSingleton<IExampleExtractorRegistry, ExampleExtractorRegistry>();

        // Register synonym writers
        services.AddSingleton<IDictionaryEntrySynonymWriter>(sp =>
            new SqlDictionaryEntrySynonymWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntrySynonymWriter>>()));

        // Register synonym extractors
        services.AddSingleton<ISynonymExtractor, EnglishChineseSynonymExtractor>();
        services.AddSingleton<ISynonymExtractor, WebsterSynonymExtractor>();
        services.AddSingleton<ISynonymExtractor, CollinsSynonymExtractor>();
        services.AddSingleton<GenericSynonymExtractor>();

        // Register synonym extractor registry
        services.AddSingleton<ISynonymExtractorRegistry, SynonymExtractorRegistry>();

        // Register SqlParsedDefinitionWriter
        services.AddSingleton<SqlParsedDefinitionWriter>(sp =>
            new SqlParsedDefinitionWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>()));

        // Register other parsing-related services
        services.AddSingleton<SqlDictionaryEntryCrossReferenceWriter>(sp =>
            new SqlDictionaryEntryCrossReferenceWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>()));

        services.AddSingleton<SqlDictionaryAliasWriter>(sp =>
            new SqlDictionaryAliasWriter(connectionString));

        services.AddSingleton<SqlDictionaryEntryVariantWriter>(sp =>
            new SqlDictionaryEntryVariantWriter(connectionString));

        // Register etymology extractors
        services.AddSingleton<IEtymologyExtractor, WebsterEtymologyExtractor>();
        services.AddSingleton<IEtymologyExtractor, EnglishChineseEtymologyExtractor>();
        services.AddSingleton<GenericEtymologyExtractor>();

        // Register etymology extractor registry
        services.AddSingleton<IEtymologyExtractorRegistry, EtymologyExtractorRegistry>();

        // Register DictionaryParsedDefinitionProcessor with ALL dependencies
        services.AddSingleton<DictionaryParsedDefinitionProcessor>(sp =>
            new DictionaryParsedDefinitionProcessor(
                connectionString,
                sp.GetRequiredService<IDictionaryDefinitionParser>(),
                sp.GetRequiredService<SqlParsedDefinitionWriter>(),
                sp.GetRequiredService<SqlDictionaryEntryCrossReferenceWriter>(),
                sp.GetRequiredService<SqlDictionaryAliasWriter>(),
                sp.GetRequiredService<IEntryEtymologyWriter>(),
                sp.GetRequiredService<SqlDictionaryEntryVariantWriter>(),
                sp.GetRequiredService<IDictionaryEntryExampleWriter>(),
                sp.GetRequiredService<IExampleExtractorRegistry>(),
                sp.GetRequiredService<ISynonymExtractorRegistry>(),
                sp.GetRequiredService<IDictionaryEntrySynonymWriter>(),
                sp.GetRequiredService<IEtymologyExtractorRegistry>(), // NEW
                sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()));

        return services;
    }
}