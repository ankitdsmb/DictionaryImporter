using DictionaryImporter.Gateway.Grammar.Configuration;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Correctors;
using DictionaryImporter.Gateway.Grammar.Engines;
using DictionaryImporter.Gateway.Grammar.Feature;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LanguageDetector = DictionaryImporter.Gateway.Grammar.Engines.LanguageDetector;

namespace DictionaryImporter.Gateway.Grammar.Extensions
{
    internal static class GrammarRegistrationExtensions
    {
        public static IServiceCollection AddGrammarCorrection(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var enabled = configuration.GetValue("Grammar:Enabled", false);
            if (!enabled)
            {
                services.AddSingleton<IGrammarFeature, NoOpGrammarFeature>();
                services.AddSingleton<IGrammarCorrector, NoOpGrammarCorrector>();
                return services;
            }

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
                        sp.GetRequiredService<LanguageDetector>(),
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
            services.TryAddSingleton(sp =>
                new GrammarCorrectorChain(
                    [
                        sp.GetRequiredService<CustomRuleCorrectorAdapter>(),
                        sp.GetRequiredService<HunspellCorrectorAdapter>(),
                        sp.GetRequiredService<LanguageToolGrammarCorrector>()
                    ],
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
}