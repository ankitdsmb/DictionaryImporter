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

            services.AddIpaConfiguration(configuration);

            services
                .AddPersistence(connectionString)
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