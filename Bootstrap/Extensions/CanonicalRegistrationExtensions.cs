using DictionaryImporter.Infrastructure.Canonicalization;

namespace DictionaryImporter.Bootstrap.Extensions
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