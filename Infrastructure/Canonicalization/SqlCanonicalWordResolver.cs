namespace DictionaryImporter.Infrastructure.Canonicalization;

public sealed class SqlCanonicalWordResolver(
    string connectionString,
    ILogger<SqlCanonicalWordResolver> logger)
    : ICanonicalWordResolver
{
    public async Task ResolveAsync(
        string sourceCode,
        CancellationToken ct)
    {
        await using var conn =
            new SqlConnection(connectionString);

        await conn.OpenAsync(ct);

        await using var tx =
            await conn.BeginTransactionAsync(ct);

        try
        {
            var totalEntries =
                await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode;
                    """,
                    new { SourceCode = sourceCode },
                    tx);

            logger.LogInformation(
                "CanonicalResolver started | Source={Source} | Entries={Count}",
                sourceCode,
                totalEntries);

            var inserted =
                await conn.ExecuteAsync(
                    """
                    INSERT INTO dbo.CanonicalWord (NormalizedWord)
                    SELECT DISTINCT e.NormalizedWord
                    FROM dbo.DictionaryEntry e
                    LEFT JOIN dbo.CanonicalWord c
                        ON c.NormalizedWord = e.NormalizedWord
                    WHERE c.CanonicalWordId IS NULL;
                    """,
                    transaction: tx);

            logger.LogInformation(
                "CanonicalResolver | Inserted {Count} new canonical words",
                inserted);

            var updated =
                await conn.ExecuteAsync(
                    """
                    UPDATE e
                    SET CanonicalWordId = c.CanonicalWordId
                    FROM dbo.DictionaryEntry e
                    INNER JOIN dbo.CanonicalWord c
                        ON c.NormalizedWord = e.NormalizedWord
                    WHERE e.CanonicalWordId IS NULL
                      AND e.SourceCode = @SourceCode;
                    """,
                    new { SourceCode = sourceCode },
                    tx);

            logger.LogInformation(
                "CanonicalResolver | Linked {Count} entries to canonical words",
                updated);

            await tx.CommitAsync(ct);

            logger.LogInformation(
                "CanonicalResolver completed successfully | Source={Source}",
                sourceCode);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);

            logger.LogError(
                ex,
                "CanonicalResolver FAILED | Source={Source}",
                sourceCode);

            throw;
        }
    }
}