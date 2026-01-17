using DictionaryImporter.Infrastructure;

namespace DictionaryImporter.Bootstrap.Extensions
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

            services.AddSingleton<IEntryEtymologyWriter>(sp =>
                new SqlDictionaryEntryEtymologyWriter(
                    connectionString,
                    sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

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