namespace DictionaryImporter.Infrastructure.PostProcessing;

public sealed class DictionaryPostProcessor
{
    private readonly string _connectionString;
    private readonly ILogger<DictionaryPostProcessor> _logger;
    private readonly IPartOfSpeechInfererV2 _posInferer;

    public DictionaryPostProcessor(
        string connectionString,
        IPartOfSpeechInfererV2 posInferer,
        ILogger<DictionaryPostProcessor> logger)
    {
        _connectionString = connectionString;
        _posInferer = posInferer;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        string sourceCode,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await InferPartOfSpeech(conn, sourceCode, ct);
        await PersistPartOfSpeechHistory(conn, sourceCode, ct);
        await ExtractSynonymsFromCrossReferences(conn, sourceCode, ct);

        _logger.LogInformation(
            "Post-processing completed | Source={SourceCode}",
            sourceCode);
    }

    // --------------------------------------------------
    // 1. PART OF SPEECH INFERENCE
    // --------------------------------------------------
    private async Task InferPartOfSpeech(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        const string sql = """
                           WITH RankedDefinitions AS
                           (
                               SELECT
                                   e.DictionaryEntryId,
                                   p.Definition,
                                   ROW_NUMBER() OVER
                                   (
                                       PARTITION BY e.DictionaryEntryId
                                       ORDER BY
                                           CASE
                                               WHEN p.ParentParsedId IS NULL THEN 0
                                               WHEN p.SenseNumber IS NOT NULL THEN 1
                                               ELSE 2
                                           END
                                   ) AS rn
                               FROM dbo.DictionaryEntry e
                               JOIN dbo.DictionaryEntryParsed p
                                   ON p.DictionaryEntryId = e.DictionaryEntryId
                               WHERE e.SourceCode = @SourceCode
                                 AND (e.PartOfSpeech IS NULL OR e.PartOfSpeech = 'unk')
                           )
                           SELECT
                               DictionaryEntryId,
                               Definition
                           FROM RankedDefinitions
                           WHERE rn = 1;
                           """;

        var rows =
            await conn.QueryAsync<(long Id, string Definition)>(
                sql,
                new { SourceCode = sourceCode });

        var updated = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var result = _posInferer.InferWithConfidence(row.Definition);
            if (result.Pos == "unk")
                continue;

            var affected =
                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.DictionaryEntry
                    SET
                        PartOfSpeech = @Pos,
                        PartOfSpeechConfidence = @Confidence
                    WHERE DictionaryEntryId = @Id
                      AND (PartOfSpeech IS NULL OR PartOfSpeech = 'unk');
                    """,
                    new
                    {
                        row.Id,
                        result.Pos,
                        result.Confidence
                    });

            if (affected > 0)
                updated++;
        }

        _logger.LogInformation(
            "POS inferred | Source={SourceCode} | Updated={Updated}",
            sourceCode,
            updated);
    }

    // --------------------------------------------------
    // 2. POS HISTORY (Source column is CORRECT)
    // --------------------------------------------------
    private async Task PersistPartOfSpeechHistory(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        var rows =
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.DictionaryEntryPartOfSpeech
                (
                    DictionaryEntryId,
                    PartOfSpeech,
                    Confidence,
                    Source,
                    CreatedUtc
                )
                SELECT
                    e.DictionaryEntryId,
                    LOWER(e.PartOfSpeech),
                    e.PartOfSpeechConfidence,
                    e.SourceCode,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntry e
                WHERE e.SourceCode = @SourceCode
                  AND e.PartOfSpeech IS NOT NULL
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DictionaryEntryPartOfSpeech p
                    WHERE p.DictionaryEntryId = e.DictionaryEntryId
                      AND p.PartOfSpeech = LOWER(e.PartOfSpeech)
                );
                """,
                new { SourceCode = sourceCode });

        _logger.LogInformation(
            "POS history persisted | Source={SourceCode} | Rows={Rows}",
            sourceCode,
            rows);
    }

    // --------------------------------------------------
    // 3. SYNONYMS (Source column is CORRECT)
    // --------------------------------------------------
    private async Task ExtractSynonymsFromCrossReferences(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        var rows =
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.DictionaryEntrySynonym
                (
                    DictionaryEntryParsedId,
                    SynonymText,
                    Source,
                    CreatedUtc
                )
                SELECT DISTINCT
                    cr.SourceParsedId,
                    LOWER(cr.TargetWord),
                    e.SourceCode,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryCrossReference cr
                JOIN dbo.DictionaryEntryParsed p
                    ON p.DictionaryEntryParsedId = cr.SourceParsedId
                JOIN dbo.DictionaryEntry e
                    ON e.DictionaryEntryId = p.DictionaryEntryId
                WHERE e.SourceCode = @SourceCode
                  AND cr.ReferenceType IN ('See','SeeAlso')
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DictionaryEntrySynonym s
                    WHERE s.DictionaryEntryParsedId = cr.SourceParsedId
                      AND s.SynonymText = LOWER(cr.TargetWord)
                );
                """,
                new { SourceCode = sourceCode });

        _logger.LogInformation(
            "Synonyms extracted | Source={SourceCode} | Rows={Rows}",
            sourceCode,
            rows);
    }
}