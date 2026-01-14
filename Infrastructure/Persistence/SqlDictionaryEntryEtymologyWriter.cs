namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryEtymologyWriter(string connectionString) : IEntryEtymologyWriter
{
    public async Task WriteAsync(
        DictionaryEntryEtymology etymology,
        CancellationToken ct)
    {
        const string sql =
            """
            INSERT INTO dbo.DictionaryEntryEtymology
            (DictionaryEntryId, EtymologyText, LanguageCode, CreatedUtc)
            VALUES
            (
                @DictionaryEntryId,
                @EtymologyText,
                @LanguageCode,
                @CreatedUtc
            )
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