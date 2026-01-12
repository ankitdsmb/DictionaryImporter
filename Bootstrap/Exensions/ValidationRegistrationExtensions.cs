using DictionaryImporter.Core.Linguistics;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Infrastructure.PostProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap.Exensions;

internal static class ValidationRegistrationExtensions
{
    public static IServiceCollection AddValidation(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddTransient<
            IDictionaryEntryValidator,
            DefaultDictionaryEntryValidator>();

        services.AddSingleton(sp =>
            new DictionaryEntryLinguisticEnricher(
                connectionString,
                sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                sp.GetRequiredService<ILogger<DictionaryEntryLinguisticEnricher>>()));

        return services;
    }
}