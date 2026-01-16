namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryEtymologyWriter : IEntryEtymologyWriter
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlDictionaryEntryEtymologyWriter> _logger;

        public SqlDictionaryEntryEtymologyWriter(
            string connectionString,
            ILogger<SqlDictionaryEntryEtymologyWriter> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task WriteAsync(DictionaryEntryEtymology etymology, CancellationToken ct)
        {
            if (etymology == null)
                throw new ArgumentNullException(nameof(etymology));

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            try
            {
                // FIXED: Check if etymology already exists before inserting
                const string checkSql = """
                    SELECT COUNT(1)
                    FROM dbo.DictionaryEntryEtymology
                    WHERE DictionaryEntryId = @DictionaryEntryId;
                    """;

                var exists = await connection.ExecuteScalarAsync<bool>(
                    new CommandDefinition(
                        checkSql,
                        new { etymology.DictionaryEntryId },
                        cancellationToken: ct));

                if (exists)
                {
                    // Update existing record instead of inserting
                    const string updateSql = """
                        UPDATE dbo.DictionaryEntryEtymology
                        SET EtymologyText = @EtymologyText,
                            LanguageCode = @LanguageCode,
                            CreatedUtc = SYSUTCDATETIME()
                        WHERE DictionaryEntryId = @DictionaryEntryId;
                        """;

                    var affected = await connection.ExecuteAsync(
                        new CommandDefinition(
                            updateSql,
                            etymology,
                            cancellationToken: ct));

                    _logger.LogDebug(
                        "Updated existing etymology for DictionaryEntryId={DictionaryEntryId}, RowsAffected={Rows}",
                        etymology.DictionaryEntryId, affected);
                }
                else
                {
                    // Insert new record
                    const string insertSql = """
                        INSERT INTO dbo.DictionaryEntryEtymology (
                            DictionaryEntryId, EtymologyText, LanguageCode, CreatedUtc
                        ) VALUES (
                            @DictionaryEntryId, @EtymologyText, @LanguageCode, SYSUTCDATETIME()
                        );
                        """;

                    var affected = await connection.ExecuteAsync(
                        new CommandDefinition(
                            insertSql,
                            etymology,
                            cancellationToken: ct));

                    _logger.LogDebug(
                        "Inserted new etymology for DictionaryEntryId={DictionaryEntryId}, RowsAffected={Rows}",
                        etymology.DictionaryEntryId, affected);
                }
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627) // Unique constraint violation
            {
                // Handle duplicate key gracefully
                _logger.LogWarning(
                    ex,
                    "Duplicate etymology entry for DictionaryEntryId={DictionaryEntryId}. Using UPSERT approach.",
                    etymology.DictionaryEntryId);

                // Try UPSERT approach
                await UpsertEtymology(connection, etymology, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to write etymology for DictionaryEntryId={DictionaryEntryId}",
                    etymology.DictionaryEntryId);
                throw;
            }
        }

        private async Task UpsertEtymology(
            SqlConnection connection,
            DictionaryEntryEtymology etymology,
            CancellationToken ct)
        {
            const string upsertSql = """
                MERGE dbo.DictionaryEntryEtymology AS target
                USING (SELECT @DictionaryEntryId AS DictionaryEntryId) AS source
                ON target.DictionaryEntryId = source.DictionaryEntryId
                WHEN MATCHED THEN
                    UPDATE SET
                        EtymologyText = @EtymologyText,
                        LanguageCode = @LanguageCode,
                        CreatedUtc = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (DictionaryEntryId, EtymologyText, LanguageCode, CreatedUtc)
                    VALUES (@DictionaryEntryId, @EtymologyText, @LanguageCode, SYSUTCDATETIME());
                """;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    upsertSql,
                    etymology,
                    cancellationToken: ct));
        }
    }
}