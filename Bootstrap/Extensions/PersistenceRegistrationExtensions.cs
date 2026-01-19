using DictionaryImporter.Infrastructure;

namespace DictionaryImporter.Bootstrap.Extensions
{
    internal static class PersistenceRegistrationExtensions
    {
        public static IServiceCollection AddPersistence(
            this IServiceCollection services,
            string connectionString)
        {
            //// FIXED: Register with batcher parameter
            //services.AddSingleton<IStagingLoader>(sp => new SqlDictionaryEntryStagingLoader(
            //    connectionString,
            //    sp.GetRequiredService<ILogger<SqlDictionaryEntryStagingLoader>>()));

            //services.AddSingleton<IDataLoader, StagingDataLoaderAdapter>();

            //services.AddSingleton<IEntryEtymologyWriter>(sp => new SqlDictionaryEntryEtymologyWriter(
            //    connectionString,
            //    sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

            //services.AddSingleton<IDataMergeExecutor>(sp => new SqlDictionaryEntryMergeExecutor(
            //    connectionString,
            //    sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

            //services.AddSingleton<IPostMergeVerifier>(sp => new SqlPostMergeVerifier(
            //    connectionString,
            //    sp.GetRequiredService<ILogger<SqlPostMergeVerifier>>()));

            //// FIXED: Register SqlDictionaryEntrySynonymWriter WITH batcher
            //services.AddSingleton<IDictionaryEntrySynonymWriter>(sp =>
            //{
            //    var logger = sp.GetRequiredService<ILogger<SqlDictionaryEntrySynonymWriter>>();
            //    var batcher = sp.GetService<GenericSqlBatcher>(); // GetOptional

            //    if (batcher != null)
            //    {
            //        return new SqlDictionaryEntrySynonymWriter(connectionString, logger, batcher);
            //    }
            //    else
            //    {
            //        // Fallback if batcher not registered
            //        return new SqlDictionaryEntrySynonymWriter(connectionString, logger);
            //    }
            //});

            return services;
        }

        public static IServiceCollection AddPersistenceWithoutSynonymWriter(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddSingleton<IStagingLoader>(sp => new SqlDictionaryEntryStagingLoader(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryStagingLoader>>()));

            services.AddSingleton<IDataLoader, StagingDataLoaderAdapter>();

            services.AddSingleton<IEntryEtymologyWriter>(sp => new SqlDictionaryEntryEtymologyWriter(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryEtymologyWriter>>()));

            services.AddSingleton<IDataMergeExecutor>(sp => new SqlDictionaryEntryMergeExecutor(
                connectionString,
                sp.GetRequiredService<ILogger<SqlDictionaryEntryMergeExecutor>>()));

            services.AddSingleton<IPostMergeVerifier>(sp => new SqlPostMergeVerifier(
                connectionString,
                sp.GetRequiredService<ILogger<SqlPostMergeVerifier>>()));

            // SKIP IDictionaryEntrySynonymWriter registration

            return services;
        }
    }
}