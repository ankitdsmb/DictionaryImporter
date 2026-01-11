using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Persistence;
using DictionaryImporter.Infrastructure;
using DictionaryImporter.Infrastructure.Merge;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Infrastructure.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Exensions
{
    internal static class PersistenceRegistrationExtensions
    {
        public static IServiceCollection AddPersistence(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddSingleton<IStagingLoader>(sp =>
                new SqlDictionaryEntryStagingLoader(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryStagingLoader>>()));

            services.AddSingleton<IDataLoader, StagingDataLoaderAdapter>();

            services.AddSingleton<IEntryEtymologyWriter>(_ =>
                new SqlDictionaryEntryEtymologyWriter(connectionString));

            services.AddSingleton<IDataMergeExecutor>(sp =>
                new SqlDictionaryEntryMergeExecutor(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

            services.AddSingleton<IPostMergeVerifier>(sp =>
                new SqlPostMergeVerifier(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlPostMergeVerifier>>()));

            return services;
        }
    }
}
