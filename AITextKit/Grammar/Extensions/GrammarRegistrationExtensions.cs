using DictionaryImporter.AITextKit.Grammar.Simple;

namespace DictionaryImporter.AITextKit.Grammar.Extensions;

internal static class GrammarRegistrationExtensions
{
    public static IServiceCollection AddGrammarCorrection(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue("Grammar:Enabled", false);
        var languageToolUrl = configuration["Grammar:LanguageToolUrl"] ?? "http://localhost:2026";

        if (enabled)
        {
            services.AddSingleton<ILanguageDetector, LanguageDetector>();
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
        var connectionString = configuration.GetConnectionString("DictionaryImporter")
                               ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

        services.AddSingleton(sp =>
        {
            var grammarCorrector = sp.GetRequiredService<IGrammarCorrector>();
            var settings = new EnhancedGrammarConfiguration();
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

    private static void Decorate<TService>(this IServiceCollection services, Func<TService, IServiceProvider, TService> decorator)
    {
    }
}