using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Verification
{
    public sealed class SqlPostMergeVerifier
        : IPostMergeVerifier
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlPostMergeVerifier> _logger;

        public SqlPostMergeVerifier(
            string connectionString,
            ILogger<SqlPostMergeVerifier> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task VerifyAsync(
            string sourceCode,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "PostMergeVerifier started | Source={Source}",
                sourceCode);

            await using var conn =
                new SqlConnection(_connectionString);

            await conn.OpenAsync(ct);

            /* ----------------------------------------------------
               1. STAGING MUST BE EMPTY
            ---------------------------------------------------- */
            var stagingCount =
                await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM dbo.DictionaryEntry_Staging
                    WHERE SourceCode = @SourceCode;
                    """,
                    new { SourceCode = sourceCode });

            _logger.LogInformation(
                "PostMergeVerifier | Source={Source} | StagingRows={Count}",
                sourceCode,
                stagingCount);

            if (stagingCount > 0)
            {
                _logger.LogError(
                    "PostMergeVerifier FAILED | Source={Source} | Reason=StagingNotEmpty | Rows={Count}",
                    sourceCode,
                    stagingCount);

                throw new InvalidOperationException(
                    $"Post-merge verification failed: " +
                    $"{stagingCount} staging rows remain for source '{sourceCode}'.");
            }

            /* ----------------------------------------------------
               2. NO DUPLICATES IN FINAL TABLE
            ---------------------------------------------------- */
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

            _logger.LogInformation(
                "PostMergeVerifier | Source={Source} | DuplicateGroups={Count}",
                sourceCode,
                duplicateCount);

            if (duplicateCount > 0)
            {
                _logger.LogError(
                    "PostMergeVerifier FAILED | Source={Source} | Reason=DuplicateKeys | Count={Count}",
                    sourceCode,
                    duplicateCount);

                throw new InvalidOperationException(
                    $"Post-merge verification failed: " +
                    $"{duplicateCount} duplicate dictionary entries detected " +
                    $"for source '{sourceCode}'.");
            }

            /* ----------------------------------------------------
               3. CANONICAL LINKAGE MUST BE COMPLETE
            ---------------------------------------------------- */
            var missingCanonical =
                await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode
                      AND CanonicalWordId IS NULL;
                    """,
                    new { SourceCode = sourceCode });

            _logger.LogInformation(
                "PostMergeVerifier | Source={Source} | MissingCanonical={Count}",
                sourceCode,
                missingCanonical);

            if (missingCanonical > 0)
            {
                _logger.LogError(
                    "PostMergeVerifier FAILED | Source={Source} | Reason=MissingCanonical | Count={Count}",
                    sourceCode,
                    missingCanonical);

                throw new InvalidOperationException(
                    $"Post-merge verification failed: " +
                    $"{missingCanonical} entries missing CanonicalWordId " +
                    $"for source '{sourceCode}'.");
            }

            _logger.LogInformation(
                "PostMergeVerifier PASSED | Source={Source}",
                sourceCode);
        }
    }
}
