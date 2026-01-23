namespace DictionaryImporter.Bootstrap.Extensions
{
    internal static class BatchRegistrationExtensions
    {
        public static IServiceCollection AddSqlBatching(
            this IServiceCollection services,
            string connectionString)
        {
            // 1. Register batcher (safe)
            services.AddSingleton<GenericSqlBatcher>(sp =>
                new GenericSqlBatcher(
                    connectionString,
                    sp.GetRequiredService<ILogger<GenericSqlBatcher>>()));

            // ✅ FIX: DO NOT initialize BatchedDapperExtensions during registration/startup
            // BatchedDapperExtensions.Initialize(batcher);

            // 2. Register repositories
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

            // ✅ FIX: constructor requires batcher + logger
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
    }
}
