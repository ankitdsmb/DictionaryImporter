using DictionaryImporter.Core.Canonical;
using DictionaryImporter.Infrastructure.Canonical;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Exensions
{
    internal static class CanonicalRegistrationExtensions
    {
        public static IServiceCollection AddCanonical(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddSingleton<ICanonicalWordResolver>(sp =>
                new SqlCanonicalWordResolver(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlCanonicalWordResolver>>()));

            return services;
        }
    }
}
