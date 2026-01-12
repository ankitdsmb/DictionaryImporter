namespace DictionaryImporter.Infrastructure.Persistence;

internal static class CanonicalWordLookup
{
    public static async Task<long?> GetCanonicalWordIdAsync(
        SqlConnection conn,
        string word,
        CancellationToken ct)
    {
        const string sql = """
                           SELECT CanonicalWordId
                           FROM dbo.CanonicalWord
                           WHERE NormalizedWord = @Word;
                           """;

        return await conn.ExecuteScalarAsync<long?>(
            new CommandDefinition(
                sql,
                new { Word = word.ToLowerInvariant() },
                cancellationToken: ct));
    }
}