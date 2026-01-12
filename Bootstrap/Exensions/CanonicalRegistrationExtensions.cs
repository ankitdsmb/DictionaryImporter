using DictionaryImporter.Core.Canonical;
using DictionaryImporter.Infrastructure.Canonicalization;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap.Exensions;

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