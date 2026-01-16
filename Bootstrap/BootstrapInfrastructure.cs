using DictionaryImporter.AITextKit.AI.Extensions;
using DictionaryImporter.AITextKit.Grammar.Extensions;
using DictionaryImporter.Bootstrap.Extensions;

namespace DictionaryImporter.Bootstrap;

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
            .AddGrammarCorrection(configuration)
            .AddGrammarCorrectionStep(configuration)
            .AddAiOrchestration(configuration);
        ;
    }
}