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

            // Add non-English text services
            services.AddNonEnglishTextServices(connectionString);

            // ✅ CRITICAL FIX: Register Grammar BEFORE Parsing
            services
                .AddIpaConfiguration(configuration)
                .AddPersistenceWithoutSynonymWriter(connectionString)
                .AddCanonical(connectionString)
                .AddValidation(connectionString)
                .AddLinguistics()
                .AddGrammar(configuration)          // ← MOVED BEFORE AddParsing
                .AddParsing(connectionString)       // ← Now has all dependencies
                .AddGraph(connectionString)
                .AddConcepts(connectionString)
                .AddIpa(connectionString)
                .AddDistributedMemoryCache()
                .AddDictionaryImporterAiGateway(configuration);
        }
    }
}