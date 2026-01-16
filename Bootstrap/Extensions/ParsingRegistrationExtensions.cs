using DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry;
using WebsterEtymologyExtractor =
    DictionaryImporter.Infrastructure.Parsing.EtymologyExtractor.WebsterEtymologyExtractor;
using WebsterSynonymExtractor = DictionaryImporter.Infrastructure.Parsing.SynonymExtractor.WebsterSynonymExtractor;

namespace DictionaryImporter.Bootstrap.Extensions;

public static class ParsingRegistrationExtensions
{
    public static IServiceCollection AddParsing(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IDictionaryDefinitionParser, OxfordDefinitionParser>();

        services.AddSingleton<IDictionaryEntryExampleWriter>(sp =>
            new SqlDictionaryEntryExampleWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>()));

        services.AddSingleton<IExampleExtractor, WebsterExampleExtractor>();
        services.AddSingleton<IExampleExtractor, EnglishChineseExampleExtractor>();
        services.AddSingleton<GenericExampleExtractor>();

        services.AddSingleton<IExampleExtractorRegistry, ExampleExtractorRegistry>();

        services.AddSingleton<IDictionaryEntrySynonymWriter>(sp =>
            new SqlDictionaryEntrySynonymWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntrySynonymWriter>>()));

        services.AddSingleton<ISynonymExtractor, EnglishChineseSynonymExtractor>();
        services.AddSingleton<ISynonymExtractor, WebsterSynonymExtractor>();
        services.AddSingleton<ISynonymExtractor, CollinsSynonymExtractor>();
        services.AddSingleton<GenericSynonymExtractor>();

        services.AddSingleton<ISynonymExtractorRegistry, SynonymExtractorRegistry>();

        services.AddSingleton(sp =>
            new SqlParsedDefinitionWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>()));

        services.AddSingleton(sp =>
            new SqlDictionaryEntryCrossReferenceWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>()));

        services.AddSingleton(sp =>
            new SqlDictionaryAliasWriter(connectionString));

        services.AddSingleton(sp =>
            new SqlDictionaryEntryVariantWriter(connectionString));

        services.AddSingleton<IEtymologyExtractor, WebsterEtymologyExtractor>();
        services.AddSingleton<IEtymologyExtractor, EnglishChineseEtymologyExtractor>();
        services.AddSingleton<GenericEtymologyExtractor>();

        services.AddSingleton<IEtymologyExtractorRegistry, EtymologyExtractorRegistry>();

        services.AddSingleton(sp =>
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
                sp.GetRequiredService<IEtymologyExtractorRegistry>(), sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()));

        return services;
    }
}