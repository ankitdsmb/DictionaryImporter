namespace DictionaryImporter.Bootstrap.Extensions;

internal static class SimpleBatchRegistrationExtensionsy
{
    public static IServiceCollection AddSimpleSqlBatching(
        this IServiceCollection services,
        string connectionString)
    {
        // ✅ REQUIRED: Stored Procedure Executor
        // NOTE: If already registered elsewhere, multiple registrations are not fatal,
        // but recommended is to keep it registered only once globally.
        services.AddSingleton<ISqlStoredProcedureExecutor>(_ =>
            new SqlStoredProcedureExecutor(connectionString));

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
            var exec = sp.GetRequiredService<ISqlStoredProcedureExecutor>();
            return new SqlDictionaryEntrySynonymWriter(connectionString, logger, batcher, exec);
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
            var exec = sp.GetRequiredService<ISqlStoredProcedureExecutor>();
            return new SqlDictionaryEntryCrossReferenceWriter(exec, logger);
        });

        // ✅ FIX: SqlDictionaryAliasWriter now requires ISqlStoredProcedureExecutor
        services.AddTransient<SqlDictionaryAliasWriter>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SqlDictionaryAliasWriter>>();
            var exec = sp.GetRequiredService<ISqlStoredProcedureExecutor>();
            return new SqlDictionaryAliasWriter(exec, logger);
        });
    }
}