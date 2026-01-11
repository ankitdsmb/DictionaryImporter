using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Infrastructure.PostProcessing.Enrichment;
using DictionaryImporter.Infrastructure.PostProcessing.Verification;
using DictionaryImporter.Infrastructure.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Exensions
{
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
}
