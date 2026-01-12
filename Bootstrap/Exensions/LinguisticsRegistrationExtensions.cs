using DictionaryImporter.Infrastructure.Linguistics;

namespace DictionaryImporter.Bootstrap.Exensions;

internal static class LinguisticsRegistrationExtensions
{
    public static IServiceCollection AddLinguistics(
        this IServiceCollection services)
    {
        services.AddSingleton<
            IPartOfSpeechInfererV2,
            ParsedDefinitionPartOfSpeechInfererV2>();

        return services;
    }
}