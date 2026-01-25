using DictionaryImporter.Core.Rewrite;
using DictionaryImporter.Gateway.Grammar.Configuration;
using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Correctors;
using DictionaryImporter.Gateway.Grammar.Engines;
using DictionaryImporter.Gateway.Grammar.Feature;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LanguageDetector = DictionaryImporter.Gateway.Grammar.Engines.LanguageDetector;

namespace DictionaryImporter.Gateway.Grammar.Extensions;

internal static class GrammarRegistrationExtensions
{
    public static IServiceCollection AddGrammarCorrection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var enabled = configuration.GetValue("Grammar:Enabled", false);

        var settings = new EnhancedGrammarConfiguration();
        configuration.GetSection("Grammar").Bind(settings);

        services.TryAddSingleton(settings);
        services.TryAddSingleton<ILanguageDetector, LanguageDetector>();

        var connectionString = configuration.GetConnectionString("DictionaryImporter")
                               ?? throw new InvalidOperationException("Connection string 'DictionaryImporter' not configured");

        //RewriteMap options
        services.Configure<RewriteMapEngineOptions>(
            configuration.GetSection("RewriteMap"));

        // RewriteMap services
        services.TryAddSingleton<IRewriteMapRepository>(sp =>
            new SqlRewriteMapRepository(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlRewriteMapRepository>>()));

        services.TryAddSingleton<RewriteMapEngine>();

        //Humanizer-safe dictionary formatter
        services.TryAddSingleton<DictionaryHumanizer>();

        //Ambient rewrite context (AsyncLocal)
        services.TryAddSingleton<IRewriteContextAccessor, RewriteContextAccessor>();

        if (!enabled)
        {
            services.TryAddSingleton<IGrammarCorrector, NoOpGrammarCorrector>();
            services.TryAddSingleton<IGrammarFeature, NoOpGrammarFeature>();
            return services;
        }

        var languageToolUrl = configuration["Grammar:LanguageToolUrl"] ?? "http://localhost:2026";
        var patternRulesPath = configuration["Grammar:PatternRulesPath"] ?? "Gateway/Grammar/Configuration/grammar-rules.json";
        var dictionaryRewriteRulesPath = configuration["Grammar:DictionaryRewriteRulesPath"] ?? "Gateway/Grammar/Configuration/dictionary-rewrite-rules.json";

        services.TryAddSingleton<ICustomGrammarRuleEngine>(_ =>
            new CustomGrammarRuleEngine(new CustomRuleEngine(patternRulesPath)));

        services.TryAddSingleton<ICustomDictionaryRewriteRuleEngine>(_ =>
            new CustomDictionaryRewriteRuleEngine(new CustomRuleEngine(dictionaryRewriteRulesPath)));

        services.TryAddSingleton<CustomRuleCorrectorAdapter>();
        services.TryAddSingleton<HunspellCorrectorAdapter>();

        //LanguageTool corrector (patched constructor - optional DI dependencies)
        services.TryAddSingleton<LanguageToolGrammarCorrector>(sp =>
            new LanguageToolGrammarCorrector(
                languageToolUrl: languageToolUrl,
                logger: sp.GetRequiredService<ILogger<LanguageToolGrammarCorrector>>(),
                rewriteContextAccessor: sp.GetService<IRewriteContextAccessor>(),
                rewriteRuleHitRepository: sp.GetService<IRewriteRuleHitRepository>()));

        //Dictionary rewrite corrector (JsonRegex hit tracking + optional hit repo)
        services.TryAddSingleton<DictionaryRewriteCorrectorAdapter>(sp =>
            new DictionaryRewriteCorrectorAdapter(
                engineWrapper: sp.GetRequiredService<ICustomDictionaryRewriteRuleEngine>(),
                rewriteMapEngine: sp.GetRequiredService<RewriteMapEngine>(),
                dictionaryHumanizer: sp.GetRequiredService<DictionaryHumanizer>(),
                rewriteContextAccessor: sp.GetRequiredService<IRewriteContextAccessor>(),
                logger: sp.GetRequiredService<ILogger<DictionaryRewriteCorrectorAdapter>>(),
                hitRepository: sp.GetService<IRewriteRuleHitRepository>()));

        services.TryAddSingleton(sp => new GrammarCorrectorChain(
            [
                sp.GetRequiredService<CustomRuleCorrectorAdapter>(),
                sp.GetRequiredService<HunspellCorrectorAdapter>(),
                sp.GetRequiredService<LanguageToolGrammarCorrector>(),
                sp.GetRequiredService<DictionaryRewriteCorrectorAdapter>()
            ],
            sp.GetRequiredService<ILogger<GrammarCorrectorChain>>()));

        services.TryAddSingleton<IGrammarCorrector>(sp => new SettingsAwareGrammarCorrector(
            sp.GetRequiredService<EnhancedGrammarConfiguration>(),
            sp.GetRequiredService<GrammarCorrectorChain>(),
            sp.GetRequiredService<ILogger<SettingsAwareGrammarCorrector>>()));

        services.TryAddSingleton<IGrammarFeature>(sp => new GrammarFeature(
            connectionString,
            sp.GetRequiredService<EnhancedGrammarConfiguration>(),
            sp.GetRequiredService<ILanguageDetector>(),
            sp.GetRequiredService<IGrammarCorrector>(),
            sp.GetServices<IGrammarEngine>(),
            sp.GetRequiredService<ILogger<GrammarFeature>>()));

        return services;
    }
}