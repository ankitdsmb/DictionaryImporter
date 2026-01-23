namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryAliasWriter(
        string connectionString,
        ILogger<SqlDictionaryAliasWriter> logger)
        : IDictionaryEntryAliasWriter
    {
        private readonly string _connectionString =
            connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private readonly ILogger<SqlDictionaryAliasWriter> _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task WriteAsync(
            long dictionaryEntryParsedId,
            string alias,
            string sourceCode,
            CancellationToken ct)
        {
            if (dictionaryEntryParsedId <= 0)
                return;

            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            alias = NormalizeAlias(alias);
            if (string.IsNullOrWhiteSpace(alias))
                return;

            const string sql =
                """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DictionaryEntryAlias WITH (NOLOCK)
                    WHERE DictionaryEntryParsedId = @DictionaryEntryParsedId
                      AND AliasText = @AliasText
                      AND SourceCode = @SourceCode
                )
                BEGIN
                    INSERT INTO dbo.DictionaryEntryAlias
                    (
                        DictionaryEntryParsedId,
                        AliasText,
                        SourceCode,
                        CreatedUtc
                    )
                    VALUES
                    (
                        @DictionaryEntryParsedId,
                        @AliasText,
                        @SourceCode,
                        SYSUTCDATETIME()
                    );
                END
                """;

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            DictionaryEntryParsedId = dictionaryEntryParsedId,
                            AliasText = alias,
                            SourceCode = sourceCode
                        },
                        cancellationToken: ct));

                _logger.LogDebug(
                    "Alias inserted (if missing): {Alias} for ParsedId={ParsedId} | SourceCode={SourceCode}",
                    alias,
                    dictionaryEntryParsedId,
                    sourceCode);
            }
            catch (Exception ex)
            {
                // ✅ Never crash importer
                _logger.LogDebug(
                    ex,
                    "Failed to insert alias: {Alias} for ParsedId={ParsedId} | SourceCode={SourceCode}",
                    alias,
                    dictionaryEntryParsedId,
                    sourceCode);
            }
        }

        // NEW METHOD (added)
        private static string NormalizeAlias(string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
                return string.Empty;

            var t = alias.Trim();

            // collapse internal whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            // ignore placeholder junk
            if (t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            // hard length cap for safety
            if (t.Length > 150)
                t = t.Substring(0, 150).Trim();

            return t;
        }
    }
}
