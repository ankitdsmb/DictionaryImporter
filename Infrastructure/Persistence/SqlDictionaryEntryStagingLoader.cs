namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryStagingLoader(
    string connectionString,
    ILogger<SqlDictionaryEntryStagingLoader> logger)
    : IStagingLoader
{
    private static readonly DateTime SqlMinDate = new(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task LoadAsync(
        IEnumerable<DictionaryEntryStaging> entries,
        CancellationToken ct)
    {
        var list = entries.ToList();
        if (list.Count == 0)
            return;

        var now = DateTime.UtcNow;

        var sanitized = list.Select(e =>
                new DictionaryEntryStaging
                {
                    Word = e.Word,
                    NormalizedWord = e.NormalizedWord,
                    PartOfSpeech = e.PartOfSpeech,
                    Definition = e.Definition,
                    Etymology = e.Etymology,
                    SenseNumber = e.SenseNumber,
                    SourceCode = e.SourceCode,
                    CreatedUtc = e.CreatedUtc < SqlMinDate
                        ? now
                        : e.CreatedUtc
                })
            .ToList();

        const string sql = """
                           INSERT INTO dbo.DictionaryEntry_Staging
                           (
                               Word,
                               NormalizedWord,
                               PartOfSpeech,
                               Definition,
                               Etymology,
                               SenseNumber,
                               SourceCode,
                               CreatedUtc
                           )
                           VALUES
                           (
                               @Word,
                               @NormalizedWord,
                               @PartOfSpeech,
                               @Definition,
                               @Etymology,
                               @SenseNumber,
                               @SourceCode,
                               @CreatedUtc
                           );
                           """;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                sql,
                sanitized,
                tx);

            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Committed batch of {Count} staging rows",
                sanitized.Count);
        }
        catch
        {
            await tx.RollbackAsync(ct);

            logger.LogError(
                "Rolled back batch of {Count} staging rows",
                sanitized.Count);

            throw;
        }
    }
}