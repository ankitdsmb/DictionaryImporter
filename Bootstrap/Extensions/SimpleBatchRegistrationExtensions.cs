using DictionaryImporter.Infrastructure.Persistence.Batched;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Extensions
{
    internal static class SimpleBatchRegistrationExtensions
    {
        public static IServiceCollection AddSimpleSqlBatching(
            this IServiceCollection services,
            string connectionString)
        {
            // 1. Register generic batcher
            services.AddSingleton<GenericSqlBatcher>(sp =>
                new GenericSqlBatcher(
                    connectionString,
                    sp.GetRequiredService<ILogger<GenericSqlBatcher>>()));

            // 2. Register repositories with batcher dependency
            RegisterBatchedRepository<IDictionaryEntrySynonymWriter, SqlDictionaryEntrySynonymWriter>(
                services, connectionString);

            // Note: SqlParsedDefinitionWriter doesn't implement IParsedDefinitionWriter
            // based on your error. Check if it implements a different interface or register differently.

            return services;
        }

        private static void RegisterBatchedRepository<TService, TImplementation>(
            IServiceCollection services,
            string connectionString)
            where TService : class
            where TImplementation : class, TService
        {
            services.AddTransient<TService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<TImplementation>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();

                // Create instance with all required parameters
                return ActivatorUtilities.CreateInstance<TImplementation>(
                    sp,
                    connectionString,
                    logger,
                    batcher);
            });
        }
    }
}