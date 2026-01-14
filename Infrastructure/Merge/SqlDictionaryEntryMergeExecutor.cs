namespace DictionaryImporter.Infrastructure.Merge;

public sealed class SqlDictionaryEntryMergeExecutor(
    string connectionString,
    ILogger<SqlDictionaryEntryMergeExecutor> logger)
    : IDataMergeExecutor
{
    public async Task ExecuteAsync(
        string sourceCode,
        CancellationToken ct)
    {
        await using var connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync(ct);

        await using var tx =
            await connection.BeginTransactionAsync(ct);

        try
        {
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
                    tx);

            var duplicateCount =
                stats.TotalRows - stats.UniqueKeys;

            logger.LogInformation(
                "Staging analysis | Source={SourceCode} | Total={Total} | Unique={Unique} | Duplicates={Duplicates}",
                sourceCode,
                stats.TotalRows,
                stats.UniqueKeys,
                duplicateCount);

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
                tx,
                0);

            const string clearStagingSql = """
                                           DELETE FROM dbo.DictionaryEntry_Staging
                                           WHERE SourceCode = @SourceCode;
                                           """;

            var cleared =
                await connection.ExecuteAsync(
                    clearStagingSql,
                    new { SourceCode = sourceCode },
                    tx);

            logger.LogInformation(
                "Cleared {Count} staging rows for source {SourceCode}",
                cleared,
                sourceCode);

            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Merge completed successfully for source {SourceCode}",
                sourceCode);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);

            logger.LogError(
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