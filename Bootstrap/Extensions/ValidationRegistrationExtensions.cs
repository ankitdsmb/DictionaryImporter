namespace DictionaryImporter.Bootstrap.Extensions
{
    internal static class ValidationRegistrationExtensions
    {
        public static IServiceCollection AddValidation(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddTransient<IDictionaryEntryValidator, DefaultDictionaryEntryValidator>();

            services.AddSingleton(sp =>
                new DictionaryEntryLinguisticEnricher(
                    connectionString,
                    sp.GetRequiredService<IPartOfSpeechInfererV2>(),
                    sp.GetRequiredService<IDictionaryEntryPartOfSpeechRepository>(),
                    sp.GetRequiredService<ILogger<DictionaryEntryLinguisticEnricher>>()));

            return services;
        }
    }
}