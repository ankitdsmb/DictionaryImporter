namespace DictionaryImporter.Infrastructure.Verification;

public sealed class SqlPostMergeVerifier(
    string connectionString,
    ILogger<SqlPostMergeVerifier> logger)
    : IPostMergeVerifier
{
    public async Task VerifyAsync(
        string sourceCode,
        CancellationToken ct)
    {
        logger.LogInformation(
            "PostMergeVerifier started | Source={Source}",
            sourceCode);

        await using var conn =
            new SqlConnection(connectionString);

        await conn.OpenAsync(ct);

        var stagingCount =
            await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.DictionaryEntry_Staging
                WHERE SourceCode = @SourceCode;
                """,
                new { SourceCode = sourceCode });

        logger.LogInformation(
            "PostMergeVerifier | Source={Source} | StagingRows={Count}",
            sourceCode,
            stagingCount);

        if (stagingCount > 0)
        {
            logger.LogError(
                "PostMergeVerifier FAILED | Source={Source} | Reason=StagingNotEmpty | Rows={Count}",
                sourceCode,
                stagingCount);

            throw new InvalidOperationException(
                $"Post-merge verification failed: " +
                $"{stagingCount} staging rows remain for source '{sourceCode}'.");
        }

        var duplicateCount =
            await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM
                (
                    SELECT SourceCode, NormalizedWord, SenseNumber
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode
                    GROUP BY SourceCode, NormalizedWord, SenseNumber
                    HAVING COUNT(*) > 1
                ) d;
                """,
                new { SourceCode = sourceCode });

        logger.LogInformation(
            "PostMergeVerifier | Source={Source} | DuplicateGroups={Count}",
            sourceCode,
            duplicateCount);

        if (duplicateCount > 0)
        {
            logger.LogError(
                "PostMergeVerifier FAILED | Source={Source} | Reason=DuplicateKeys | Count={Count}",
                sourceCode,
                duplicateCount);

            throw new InvalidOperationException(
                $"Post-merge verification failed: " +
                $"{duplicateCount} duplicate dictionary entries detected " +
                $"for source '{sourceCode}'.");
        }

        var missingCanonical =
            await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.DictionaryEntry
                WHERE SourceCode = @SourceCode
                  AND CanonicalWordId IS NULL;
                """,
                new { SourceCode = sourceCode });

        logger.LogInformation(
            "PostMergeVerifier | Source={Source} | MissingCanonical={Count}",
            sourceCode,
            missingCanonical);

        if (missingCanonical > 0)
        {
            logger.LogError(
                "PostMergeVerifier FAILED | Source={Source} | Reason=MissingCanonical | Count={Count}",
                sourceCode,
                missingCanonical);

            throw new InvalidOperationException(
                $"Post-merge verification failed: " +
                $"{missingCanonical} entries missing CanonicalWordId " +
                $"for source '{sourceCode}'.");
        }

        logger.LogInformation(
            "PostMergeVerifier PASSED | Source={Source}",
            sourceCode);
    }
}