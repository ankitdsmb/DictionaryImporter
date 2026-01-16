namespace DictionaryImporter.Bootstrap.Extensions;

internal static class IpaRegistrationExtensions
{
    public static IServiceCollection AddIpa(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(_ =>
            new SqlCanonicalWordPronunciationWriter(connectionString));

        services.AddSingleton(sp =>
            new CanonicalWordIpaEnricher(
                connectionString,
                sp.GetRequiredService<SqlCanonicalWordPronunciationWriter>(),
                sp.GetRequiredService<ILogger<CanonicalWordIpaEnricher>>()));

        services.AddSingleton(sp =>
            new IpaVerificationReporter(
                connectionString,
                sp.GetRequiredService<ILogger<IpaVerificationReporter>>()));

        return services;
    }
}