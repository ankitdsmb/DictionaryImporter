using DictionaryImporter.AITextKit.Grammar.Enhanced;
using DictionaryImporter.AITextKit.Grammar.Simple;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.AITextKit.Grammar.Extensions;

internal static class GrammarRegistrationExtensions
{
    public static IServiceCollection AddGrammarCorrection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var enabled = configuration.GetValue("Grammar:Enabled", false);

        var connectionString =
            configuration.GetConnectionString("DictionaryImporter")
            ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

        var languageToolUrl =
            configuration["Grammar:LanguageToolUrl"] ?? "http://localhost:2026";

        var patternRulesPath =
            configuration["Grammar:PatternRulesPath"] ?? "GrammarRules/grammar-rules.json";

        var settings = new EnhancedGrammarConfiguration();
        configuration.GetSection("Grammar").Bind(settings);

        services.TryAddSingleton(settings);
        services.TryAddSingleton<ILanguageDetector, LanguageDetector>();

        // ------------------------------------------------------------
        // Disabled => always safe no-op
        // ------------------------------------------------------------
        if (!enabled)
        {
            services.TryAddSingleton<IGrammarCorrector, NoOpGrammarCorrector>();

            services.TryAddSingleton<IGrammarFeature>(sp =>
                new GrammarFeature(
                    connectionString,
                    sp.GetRequiredService<EnhancedGrammarConfiguration>(),
                    sp.GetRequiredService<ILanguageDetector>(),
                    sp.GetRequiredService<IGrammarCorrector>(),
                    sp.GetServices<IGrammarEngine>(),
                    sp.GetRequiredService<ILogger<GrammarFeature>>()));

            return services;
        }

        // ------------------------------------------------------------
        // ✅ CustomRuleEngine loads JSON from PatternRulesPath
        // ------------------------------------------------------------
        services.TryAddSingleton(sp => new CustomRuleEngine(patternRulesPath));

        // ------------------------------------------------------------
        // ✅ Register concrete correctors (NOT as IGrammarCorrector)
        // ------------------------------------------------------------
        services.TryAddSingleton<CustomRuleCorrectorAdapter>();
        services.TryAddSingleton<HunspellCorrectorAdapter>();

        services.TryAddSingleton(sp =>
            new LanguageToolGrammarCorrector(
                languageToolUrl,
                sp.GetRequiredService<ILogger<LanguageToolGrammarCorrector>>()));

        // ------------------------------------------------------------
        // ✅ Register chain (single instance)
        // ------------------------------------------------------------
        services.TryAddSingleton<GrammarCorrectorChain>(sp =>
            new GrammarCorrectorChain(
                new IGrammarCorrector[]
                {
                    sp.GetRequiredService<CustomRuleCorrectorAdapter>(),
                    sp.GetRequiredService<HunspellCorrectorAdapter>(),
                    sp.GetRequiredService<LanguageToolGrammarCorrector>()
                },
                sp.GetRequiredService<ILogger<GrammarCorrectorChain>>()));

        // ------------------------------------------------------------
        // ✅ Register ONE public IGrammarCorrector => Settings wrapper
        // ------------------------------------------------------------
        services.TryAddSingleton<IGrammarCorrector>(sp =>
            new SettingsAwareGrammarCorrector(
                sp.GetRequiredService<EnhancedGrammarConfiguration>(),
                sp.GetRequiredService<GrammarCorrectorChain>(),
                sp.GetRequiredService<ILogger<SettingsAwareGrammarCorrector>>()));

        // ------------------------------------------------------------
        // ✅ Single truth: GrammarFeature
        // ------------------------------------------------------------
        services.TryAddSingleton<IGrammarFeature>(sp =>
            new GrammarFeature(
                connectionString,
                sp.GetRequiredService<EnhancedGrammarConfiguration>(),
                sp.GetRequiredService<ILanguageDetector>(),
                sp.GetRequiredService<IGrammarCorrector>(), // ✅ SettingsAware -> Chain
                sp.GetServices<IGrammarEngine>(),
                sp.GetRequiredService<ILogger<GrammarFeature>>()));

        return services;
    }
}