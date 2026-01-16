namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryEtymologyWriter(string connectionString) : IEntryEtymologyWriter
{
    public async Task WriteAsync(
        DictionaryEntryEtymology etymology,
        CancellationToken ct)
    {
        const string sql =
            """
            IF NOT EXISTS (SELECT 1 FROM DictionaryEntryEtymology
                            WHERE DictionaryEntryId = @DictionaryEntryId
                            AND EtymologyText = @EtymologyText
                            AND LanguageCode = @LanguageCode)
            --BEGIN
                --UPDATE DictionaryEntryEtymology
                --SET EtymologyText = @EtymologyText,
                    --UpdatedUtc = SYSUTCDATETIME()
                --WHERE DictionaryEntryId = @EntryId;
            --END
            --ELSE
            BEGIN
                INSERT INTO dbo.DictionaryEntryEtymology
                (DictionaryEntryId, EtymologyText, LanguageCode, CreatedUtc)
                VALUES
                (
                    @DictionaryEntryId,
                    @EtymologyText,
                    @LanguageCode,
                    @CreatedUtc
                )
            END
            """;

        await using var conn =
            new SqlConnection(connectionString);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                etymology,
                cancellationToken: ct));
    }
}