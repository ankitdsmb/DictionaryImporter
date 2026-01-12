namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryEtymologyWriter
    : IEntryEtymologyWriter
{
    private readonly string _connectionString;

    public SqlDictionaryEntryEtymologyWriter(string connectionString)
    {
        _connectionString = connectionString;
    }

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
            new SqlConnection(_connectionString);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                etymology,
                cancellationToken: ct));
    }
}