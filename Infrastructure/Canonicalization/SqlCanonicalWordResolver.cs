using DictionaryImporter.Core.Canonical;

namespace DictionaryImporter.Infrastructure.Canonicalization;

public sealed class SqlCanonicalWordResolver
    : ICanonicalWordResolver
{
    private readonly string _connectionString;
    private readonly ILogger<SqlCanonicalWordResolver> _logger;

    public SqlCanonicalWordResolver(
        string connectionString,
        ILogger<SqlCanonicalWordResolver> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task ResolveAsync(
        string sourceCode,
        CancellationToken ct)
    {
        await using var conn =
            new SqlConnection(_connectionString);

        await conn.OpenAsync(ct);

        await using var tx =
            await conn.BeginTransactionAsync(ct);

        try
        {
            /* ----------------------------------------------------
               0. PRE-STATS (VISIBILITY)
            ---------------------------------------------------- */
            var totalEntries =
                await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode;
                    """,
                    new { SourceCode = sourceCode },
                    tx);

            _logger.LogInformation(
                "CanonicalResolver started | Source={Source} | Entries={Count}",
                sourceCode,
                totalEntries);

            /* ----------------------------------------------------
               1. INSERT MISSING CANONICAL WORDS
            ---------------------------------------------------- */
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

            _logger.LogInformation(
                "CanonicalResolver | Inserted {Count} new canonical words",
                inserted);

            /* ----------------------------------------------------
               2. ATTACH CANONICALWORDID (SOURCE-SCOPED)
            ---------------------------------------------------- */
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

            _logger.LogInformation(
                "CanonicalResolver | Linked {Count} entries to canonical words",
                updated);

            await tx.CommitAsync(ct);

            _logger.LogInformation(
                "CanonicalResolver completed successfully | Source={Source}",
                sourceCode);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);

            _logger.LogError(
                ex,
                "CanonicalResolver FAILED | Source={Source}",
                sourceCode);

            throw;
        }
    }
}