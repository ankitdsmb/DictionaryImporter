namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryAliasWriter(string connectionString, ILogger<SqlDictionaryAliasWriter> logger)
        : IDictionaryEntryAliasWriter
    {
        private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        private readonly ILogger<SqlDictionaryAliasWriter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // FIXED: Single proper constructor

        public async Task WriteAsync(
            long dictionaryEntryParsedId,
            string alias,
            CancellationToken ct)
        {
            const string sql =
                """
                INSERT INTO dbo.DictionaryEntryAlias
                (
                    DictionaryEntryParsedId,
                    AliasText,
                    CreatedUtc
                )
                VALUES
                (
                    @DictionaryEntryParsedId,
                    @AliasText,
                    SYSUTCDATETIME()
                )
                """;

            await using var conn = new SqlConnection(_connectionString);
            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        DictionaryEntryParsedId = dictionaryEntryParsedId,
                        AliasText = alias
                    },
                    cancellationToken: ct));

            _logger.LogDebug("Alias inserted: {Alias} for ParsedId={ParsedId}",
                alias, dictionaryEntryParsedId);
        }
    }
}