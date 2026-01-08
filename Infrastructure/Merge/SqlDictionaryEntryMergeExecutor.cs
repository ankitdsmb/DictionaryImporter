using Dapper;
using DictionaryImporter.Core.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Merge
{
    public sealed class SqlDictionaryEntryMergeExecutor
        : IDataMergeExecutor
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlDictionaryEntryMergeExecutor> _logger;

        public SqlDictionaryEntryMergeExecutor(
            string connectionString,
            ILogger<SqlDictionaryEntryMergeExecutor> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct)
        {
            await using var connection =
                new SqlConnection(_connectionString);

            await connection.OpenAsync(ct);

            await using var tx =
                await connection.BeginTransactionAsync(ct);

            try
            {
                /* =====================================================
                   1. PRE-MERGE STAGING ANALYSIS (LOG ONLY)
                ===================================================== */
                const string stagingStatsSql = """
                SELECT
                    COUNT_BIG(*) AS TotalRows,
                    COUNT_BIG(DISTINCT
                        CONCAT(SourceCode, '|', NormalizedWord, '|', SenseNumber)
                    ) AS UniqueKeys
                FROM dbo.DictionaryEntry_Staging
                WHERE SourceCode = @SourceCode;
                """

                ;

                var stats =
                    await connection.QuerySingleAsync<StagingStats>(
                        stagingStatsSql,
                        new { SourceCode = sourceCode },
                        transaction: tx);

                var duplicateCount =
                    stats.TotalRows - stats.UniqueKeys;

                _logger.LogInformation(
                    "Staging analysis | Source={SourceCode} | Total={Total} | Unique={Unique} | Duplicates={Duplicates}",
                    sourceCode,
                    stats.TotalRows,
                    stats.UniqueKeys,
                    duplicateCount);

                /* =====================================================
                   2. DEDUPLICATING IDEMPOTENT MERGE
                ===================================================== */
                const string mergeSql = """
                SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

                WITH DedupedSource AS
                (
                    SELECT
                        Word,
                        NormalizedWord,
                        PartOfSpeech,
                        Definition,
                        Etymology,
                        SenseNumber,
                        SourceCode,
                        CreatedUtc,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY
                                SourceCode,
                                NormalizedWord,
                                SenseNumber
                            ORDER BY
                                CreatedUtc DESC
                        ) AS rn
                    FROM dbo.DictionaryEntry_Staging
                    WHERE SourceCode = @SourceCode
                )
                MERGE dbo.DictionaryEntry AS Target
                USING
                (
                    SELECT
                        Word,
                        NormalizedWord,
                        PartOfSpeech,
                        Definition,
                        Etymology,
                        SenseNumber,
                        SourceCode,
                        CreatedUtc
                    FROM DedupedSource
                    WHERE rn = 1
                ) AS Source
                ON
                    Target.SourceCode     = Source.SourceCode AND
                    Target.NormalizedWord = Source.NormalizedWord AND
                    Target.SenseNumber    = Source.SenseNumber

                WHEN NOT MATCHED BY TARGET THEN
                    INSERT
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
                        Source.Word,
                        Source.NormalizedWord,
                        Source.PartOfSpeech,
                        Source.Definition,
                        Source.Etymology,
                        Source.SenseNumber,
                        Source.SourceCode,
                        Source.CreatedUtc
                    );
                """;

                await connection.ExecuteAsync(
                    mergeSql,
                    new { SourceCode = sourceCode },
                    transaction: tx,
                    commandTimeout: 0);

                /* =====================================================
                   3. SOURCE-SCOPED STAGING CLEANUP
                ===================================================== */
                const string clearStagingSql = """
                DELETE FROM dbo.DictionaryEntry_Staging
                WHERE SourceCode = @SourceCode;
                """;

                var cleared =
                    await connection.ExecuteAsync(
                        clearStagingSql,
                        new { SourceCode = sourceCode },
                        transaction: tx);

                _logger.LogInformation(
                    "Cleared {Count} staging rows for source {SourceCode}",
                    cleared,
                    sourceCode);

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Merge completed successfully for source {SourceCode}",
                    sourceCode);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);

                _logger.LogError(
                    ex,
                    "Merge failed for source {SourceCode}. Staging preserved.",
                    sourceCode);

                throw;
            }
        }

        private sealed class StagingStats
        {
            public long TotalRows { get; init; }
            public long UniqueKeys { get; init; }
        }
    }
}
