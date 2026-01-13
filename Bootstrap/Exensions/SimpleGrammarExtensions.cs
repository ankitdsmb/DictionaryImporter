// File: DictionaryImporter.Bootstrap/Exensions/SimpleGrammarExtensions.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Simple;
using DictionaryImporter.Infrastructure.Grammar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Exensions;

public static class SimpleGrammarExtensions
{
    public static IServiceCollection AddSimpleGrammarCorrection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        try
        {
            // Load settings with proper defaults
            var settings = new GrammarCorrectionSettings();
            configuration.GetSection("GrammarCorrection").Bind(settings);

            // Ensure URL is not null or empty
            if (string.IsNullOrWhiteSpace(settings.LanguageToolUrl))
            {
                settings.LanguageToolUrl = "http://localhost:2026";
            }

            services.AddSingleton(settings);

            // Register grammar corrector with null check
            services.AddSingleton<IGrammarCorrector>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SimpleGrammarCorrector>>();
                return new SimpleGrammarCorrector(settings.LanguageToolUrl, logger);
            });

            // Register grammar correction step
            services.AddSingleton<GrammarCorrectionStep>(sp =>
            {
                var connectionString = configuration.GetConnectionString("DictionaryImporter")
                    ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

                return new GrammarCorrectionStep(
                    connectionString,
                    sp.GetRequiredService<IGrammarCorrector>(),
                    settings,
                    sp.GetRequiredService<ILogger<GrammarCorrectionStep>>());
            });
        }
        catch (Exception ex)
        {
            // If registration fails, add a fallback no-op corrector
            Console.WriteLine($"Grammar correction setup failed: {ex.Message}. Using no-op fallback.");
            services.AddSingleton<IGrammarCorrector>(new NoOpGrammarCorrector());
        }

        return services;
    }
}