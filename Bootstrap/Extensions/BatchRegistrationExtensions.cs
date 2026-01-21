// File: Bootstrap/Extensions/BatchRegistrationExtensions.cs
using DictionaryImporter.Infrastructure.Persistence;
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
            services.AddSingleton<GenericSqlBatcher>(sp => new GenericSqlBatcher(
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

            // ✅ FIX: SqlParsedDefinitionWriter requires GenericSqlBatcher + ILogger
            services.AddTransient<SqlParsedDefinitionWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlParsedDefinitionWriter>>();
                var batcher = sp.GetRequiredService<GenericSqlBatcher>();
                return new SqlParsedDefinitionWriter(connectionString, batcher, logger);
            });

            services.AddTransient<SqlDictionaryEntryCrossReferenceWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlDictionaryEntryCrossReferenceWriter>>();
                return new SqlDictionaryEntryCrossReferenceWriter(connectionString, logger);
            });

            services.AddTransient<SqlDictionaryAliasWriter>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlDictionaryAliasWriter>>();
                return new SqlDictionaryAliasWriter(connectionString, logger);
            });
        }

        private class BatcherInitializer : IHostedService
        {
            private readonly GenericSqlBatcher _batcher;

            public BatcherInitializer(GenericSqlBatcher batcher)
            {
                _batcher = batcher;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                BatchedDapperExtensions.Initialize(_batcher);
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
