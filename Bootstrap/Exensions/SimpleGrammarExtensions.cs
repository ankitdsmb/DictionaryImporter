// File: DictionaryImporter/Bootstrap/Extensions/SimpleGrammarExtensions.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Simple;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap.Exensions;

public static class SimpleGrammarExtensions
{
    public static IServiceCollection AddSimpleGrammarCorrection(
        this IServiceCollection services, IConfiguration configuration)
    {
        var url = configuration["Grammar:LanguageToolUrl"] ?? "http://localhost:2026";

        services.AddSingleton<IGrammarCorrector>(sp =>
        {
            try
            {
                return new SimpleGrammarCorrector(
                    url,
                    configuration,
                    sp.GetRequiredService<ILogger<SimpleGrammarCorrector>>(),
                    sp.GetRequiredService<ILoggerFactory>());
            }
            catch (Exception ex)
            {
                var logger = sp.GetRequiredService<ILogger<SimpleGrammarCorrector>>();
                logger.LogError(ex, "Failed to create SimpleGrammarCorrector with full configuration, using simplified version");

                // Fallback to simplified constructor
                return new SimpleGrammarCorrector(
                    url,
                    sp.GetRequiredService<ILogger<SimpleGrammarCorrector>>());
            }
        });

        return services;
    }
}