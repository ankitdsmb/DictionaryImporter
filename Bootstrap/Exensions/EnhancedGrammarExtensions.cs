using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Configuration;
using DictionaryImporter.Core.Grammar.Enhanced;
using DictionaryImporter.Core.Grammar.Simple;
using DictionaryImporter.Infrastructure.Grammar.Engines;
using DictionaryImporter.Infrastructure.Grammar.Enhanced;

namespace DictionaryImporter.Bootstrap.Exensions;

public static class EnhancedGrammarExtensions
{
    public static IServiceCollection AddEnhancedGrammarCorrection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var grammarConfig = configuration.GetSection("EnhancedGrammar")
            .Get<EnhancedGrammarConfiguration>() ?? new EnhancedGrammarConfiguration();

        if (!grammarConfig.Enabled)
        {
            // Fallback to simple corrector
            services.AddSingleton<IGrammarCorrector>(new SimpleGrammarCorrector(
                grammarConfig.LanguageToolUrl,
                null));
            return services;
        }

        try
        {
            // Register individual engines
            services.AddSingleton<IGrammarEngine>(sp =>
                new LanguageToolEngine(
                    grammarConfig.LanguageToolUrl,
                    sp.GetRequiredService<ILogger<LanguageToolEngine>>()
                ));

            services.AddSingleton<IGrammarEngine>(sp =>
                new NHunspellEngine(
                    grammarConfig.HunspellDictionaryPath,
                    sp.GetRequiredService<ILogger<NHunspellEngine>>()
                ));

            services.AddSingleton<IGrammarEngine>(sp =>
                new PatternRuleEngine(
                    grammarConfig.CustomRulesPath,
                    sp.GetRequiredService<ILogger<PatternRuleEngine>>()
                ));

            // Register the pipeline
            services.AddSingleton<IGrammarPipeline>(sp =>
            {
                var engines = sp.GetServices<IGrammarEngine>().ToList();
                var pipelineConfig = grammarConfig.ToPipelineConfiguration();
                var logger = sp.GetRequiredService<ILogger<GrammarPipeline>>();

                return new GrammarPipeline(engines, pipelineConfig, logger);
            });

            // Use IGrammarPipeline as IGrammarCorrector too
            services.AddSingleton<IGrammarCorrector>(sp =>
                sp.GetRequiredService<IGrammarPipeline>());
        }
        catch (Exception ex)
        {
            // Fallback if enhanced setup fails
            Console.WriteLine($"Enhanced grammar setup failed: {ex.Message}. Using simple corrector.");
            services.AddSingleton<IGrammarCorrector>(new SimpleGrammarCorrector());
        }

        return services;
    }
}