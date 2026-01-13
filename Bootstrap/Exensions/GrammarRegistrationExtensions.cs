using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Simple;
using DictionaryImporter.Infrastructure.Grammar;
using DictionaryImporter.Infrastructure.Parsing;

namespace DictionaryImporter.Bootstrap.Exensions;

internal static class GrammarRegistrationExtensions
{
    public static IServiceCollection AddGrammarCorrection(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("Grammar:Enabled", false);
        var languageToolUrl = configuration["Grammar:LanguageToolUrl"] ?? "http://localhost:2026";

        if (enabled)
        {
            services.AddSingleton<ILanguageDetector, Core.Grammar.LanguageDetector>();
            services.AddSingleton<IGrammarCorrector>(sp =>
            {
                var languageDetector = sp.GetRequiredService<ILanguageDetector>();
                var languageToolCorrector = new LanguageToolGrammarCorrector(
                    languageToolUrl,
                    sp.GetRequiredService<ILogger<LanguageToolGrammarCorrector>>()
                );
                var logger = sp.GetRequiredService<ILogger<HybridGrammarCorrector>>();

                return new HybridGrammarCorrector(languageDetector, languageToolCorrector, logger);
            });

            // Decorate parsers as before
            services.Decorate<IDictionaryDefinitionParser>(
                (inner, sp) => new GrammarAwareDefinitionParser(
                    inner,
                    sp.GetRequiredService<IGrammarCorrector>(),
                    sp.GetRequiredService<ILogger<GrammarAwareDefinitionParser>>()
                ));
        }
        else
        {
            services.AddSingleton<IGrammarCorrector>(new NoOpGrammarCorrector());
        }

        return services;
    }

    public static IServiceCollection AddGrammarCorrectionStep(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Get the connection string
        var connectionString = configuration.GetConnectionString("DictionaryImporter")
                               ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

        // Register GrammarCorrectionStep
        services.AddSingleton<GrammarCorrectionStep>(sp =>
        {
            var grammarCorrector = sp.GetRequiredService<IGrammarCorrector>();
            var settings = new GrammarCorrectionSettings();
            configuration.GetSection("Grammar").Bind(settings);
            var logger = sp.GetRequiredService<ILogger<GrammarCorrectionStep>>();

            return new GrammarCorrectionStep(
                connectionString,
                grammarCorrector,
                settings,
                logger
            );
        });

        return services;
    }

    // Helper method for service decoration (requires Scrutor or similar)
    private static void Decorate<TService>(this IServiceCollection services, Func<TService, IServiceProvider, TService> decorator)
    {
        // Implementation depends on your DI container
        // This is a simplified version
    }
}