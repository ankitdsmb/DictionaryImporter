using DictionaryImporter.Infrastructure.PostProcessing.Enrichment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap.Exensions
{
    internal static class IpaConfigurationExtensions
    {
        public static IServiceCollection AddIpaConfiguration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton(
                configuration
                    .GetSection("IPA:Sources")
                    .Get<IReadOnlyList<IpaSourceConfig>>()
                ?? Array.Empty<IpaSourceConfig>());

            return services;
        }
    }
}
