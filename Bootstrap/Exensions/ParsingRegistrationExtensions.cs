using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Core.Persistence;
using DictionaryImporter.Infrastructure.Parsing;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Infrastructure.PostProcessing;
using DictionaryImporter.Sources.Gutenberg.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Exensions
{
    internal static class ParsingRegistrationExtensions
    {
        public static IServiceCollection AddParsing(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddSingleton<
                IDictionaryDefinitionParser,
                WebsterSubEntryParser>();

            services.AddSingleton(sp =>
                new SqlParsedDefinitionWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>()));

            services.AddSingleton(_ =>
                new SqlDictionaryAliasWriter(connectionString));

            services.AddSingleton(_ =>
                new SqlDictionaryEntrySynonymWriter(connectionString));

            services.AddSingleton(sp =>
                new SqlDictionaryEntryCrossReferenceWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>()));

            services.AddSingleton(_ =>
                new SqlDictionaryEntryVariantWriter(connectionString));

            services.AddSingleton<IDictionaryEntryExampleWriter>(sp =>
                new SqlDictionaryEntryExampleWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryExampleWriter>>()));

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
                    sp.GetRequiredService<ILogger<DictionaryParsedDefinitionProcessor>>()));

            return services;
        }
    }
}
