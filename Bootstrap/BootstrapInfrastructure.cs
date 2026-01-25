using DictionaryImporter.Bootstrap.Extensions;
using DictionaryImporter.Core.Jobs;
using DictionaryImporter.Gateway.Ai.Bootstrap;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap
{
    public static class BootstrapInfrastructure
    {
        public static void Register(IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DictionaryImporter")
                                   ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

            BootstrapLogging.Register(services);

            // ✅ Register RuleBased rewrite job (Option 2)
            services.Configure<RuleBasedRewriteJobOptions>(
                configuration.GetSection("RuleBasedRewriteJob"));
            services.AddScoped<RuleBasedRewriteJob>();

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
                .AddGrammar(configuration)          // ✅ Grammar + DictionaryRewriteCorrector are registered here
                .AddParsing(connectionString)
                .AddGraph(connectionString)
                .AddConcepts(connectionString)
                .AddIpa(connectionString)
                .AddDistributedMemoryCache()
                .AddDictionaryImporterAiGateway(configuration);
        }
    }
}