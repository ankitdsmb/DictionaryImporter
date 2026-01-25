using DictionaryImporter.Core.Text;
using DictionaryImporter.Gateway.Grammar.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap.Extensions;

internal static class GrammarRegistrationExtensions
{
    public static IServiceCollection AddGrammar(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddGrammarCorrection(configuration);

        services.Configure<OcrNormalizationOptions>(configuration.GetSection("OcrNormalization"));
        services.Configure<DictionaryTextFormattingOptions>(configuration.GetSection("TextFormatting"));

        services.AddSingleton<IOcrArtifactNormalizer, OcrArtifactNormalizer>();
        services.AddSingleton<IDefinitionNormalizer, DefinitionNormalizer>();
        services.AddSingleton<IDictionaryTextFormatter, DictionaryTextFormatter>();

        services.AddSingleton<IGrammarEnrichedTextService, GrammarEnrichedTextService>();

        return services;
    }
}