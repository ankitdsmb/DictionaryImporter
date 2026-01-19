using DictionaryImporter.Infrastructure.Persistence.Batched;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Extensions
{
    internal static class BatchRegistrationExtensions
    {
        public static IServiceCollection AddSqlBatching(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddSingleton<GenericSqlBatcher>(sp =>
                new GenericSqlBatcher(
                    connectionString,
                    sp.GetRequiredService<ILogger<GenericSqlBatcher>>()));

            services.AddSingleton(sp =>
            {
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                BatchedDapperExtensions.Initialize(batcher);
                return batcher;
            });

            RegisterRepositories(services, connectionString);
            return services;
        }

        private static void RegisterRepositories(IServiceCollection services, string connectionString)
        {
            services.AddTransient<IDictionaryEntrySynonymWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlDictionaryEntrySynonymWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlDictionaryEntrySynonymWriter(connectionString, logger, batcher);
            });

            services.AddTransient<SqlParsedDefinitionWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>();
                return new SqlParsedDefinitionWriter(connectionString, logger);
            });

            // 5. Register other repositories similarly
            services.AddTransient<SqlDictionaryEntryCrossReferenceWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>();
                return new SqlDictionaryEntryCrossReferenceWriter(connectionString, logger);
            });

            services.AddTransient<SqlDictionaryAliasWriter>(sp =>
            {
                return new SqlDictionaryAliasWriter(connectionString);
            });
        }

        private class BatcherInitializer(GenericSqlBatcher batcher) : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken)
            {
                BatchedDapperExtensions.Initialize(batcher);
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}