// File: Bootstrap/Extensions/NonEnglishTextRegistrationExtensions.cs
using DictionaryImporter.Core.Text;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap.Extensions;

internal static class NonEnglishTextRegistrationExtensions
{
    public static IServiceCollection AddNonEnglishTextServices(
        this IServiceCollection services,
        string connectionString)
    {
        // Register the non-English text storage service
        services.AddSingleton<INonEnglishTextStorage>(sp =>
            new SqlNonEnglishTextStorage(
                sp.GetRequiredService<ISqlStoredProcedureExecutor>(),
                sp.GetRequiredService<ILogger<SqlNonEnglishTextStorage>>()));

        // Register the language detection service
        services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();

        return services;
    }
}