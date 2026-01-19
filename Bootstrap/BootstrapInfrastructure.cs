using DictionaryImporter.Bootstrap.Extensions;
using DictionaryImporter.Gateway.Ai.Bootstrap;

namespace DictionaryImporter.Bootstrap
{
    public static class BootstrapInfrastructure
    {
        public static void Register(IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DictionaryImporter")
                                   ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

            BootstrapLogging.Register(services);

            // Add SQL batching
            services.AddSqlBatching(connectionString);

            // Modified AddPersistence to skip IDictionaryEntrySynonymWriter registration
            services
                .AddIpaConfiguration(configuration)
                .AddPersistenceWithoutSynonymWriter(connectionString)
                .AddCanonical(connectionString)
                .AddValidation(connectionString)
                .AddLinguistics()
                .AddParsing(connectionString)
                .AddGraph(connectionString)
                .AddConcepts(connectionString)
                .AddIpa(connectionString)
                .AddParsing(connectionString)
                .AddGrammar(configuration)
                .AddDistributedMemoryCache()
                .AddDictionaryImporterAiGateway(configuration);
        }
    }
}