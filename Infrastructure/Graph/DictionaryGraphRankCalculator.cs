namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryGraphRankCalculator(
    string connectionString,
    ILogger<DictionaryGraphRankCalculator> logger)
{
    public async Task CalculateAsync(
        CancellationToken ct)
    {
        logger.LogInformation(
            "GraphRankCalculator started");

        var sw = Stopwatch.StartNew();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var conceptRankRows =
            await CalculateConceptRank(conn, ct);

        logger.LogInformation(
            "GraphRankCalculator | Stage=ConceptRank | AffectedRows={Rows}",
            conceptRankRows);

        var senseRankRows =
            await CalculateSenseRank(conn, ct);

        logger.LogInformation(
            "GraphRankCalculator | Stage=SenseRank | AffectedRows={Rows}",
            senseRankRows);

        var wordRankRows =
            await CalculateWordRank(conn, ct);

        logger.LogInformation(
            "GraphRankCalculator | Stage=WordRank | AffectedRows={Rows}",
            wordRankRows);

        sw.Stop();

        logger.LogInformation(
            "GraphRankCalculator completed | DurationMs={Duration}",
            sw.ElapsedMilliseconds);
    }

    private async Task<int> CalculateConceptRank(
        SqlConnection conn,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await conn.ExecuteAsync(
            new CommandDefinition(
                """
                ;WITH SenseStats AS
                (
                    SELECT
                        c.ConceptId,
                        COUNT(DISTINCT g.FromNodeId) AS SenseCount
                    FROM dbo.Concept c
                    LEFT JOIN dbo.GraphEdge g
                        ON g.ToNodeId = CONCAT('Concept:', c.ConceptId)
                       AND g.RelationType = 'BELONGS_TO'
                    GROUP BY c.ConceptId
                ),
                CrossRefStats AS
                (
                    SELECT
                        c.ConceptId,
                        COUNT(DISTINCT cr.FromNodeId) AS CrossRefCount
                    FROM dbo.Concept c
                    JOIN dbo.GraphEdge g
                        ON g.ToNodeId = CONCAT('Concept:', c.ConceptId)
                       AND g.RelationType = 'BELONGS_TO'
                    JOIN dbo.GraphEdge cr
                        ON cr.FromNodeId = g.FromNodeId
                       AND cr.RelationType IN ('SEE','RELATED_TO','COMPARE')
                    GROUP BY c.ConceptId
                )
                MERGE dbo.ConceptRank AS t
                USING
                (
                    SELECT
                        c.ConceptId,
                        (
                            CASE
                                WHEN ISNULL(s.SenseCount, 0) / 5.0 > 1.0
                                    THEN 1.0
                                ELSE ISNULL(s.SenseCount, 0) / 5.0
                            END * 0.55 +

                            CASE
                                WHEN ISNULL(s.SenseCount, 0) = 0 THEN 0
                                WHEN (ISNULL(cr.CrossRefCount, 0) * 1.0 / s.SenseCount) / 4.0 > 1.0
                                    THEN 1.0
                                ELSE (ISNULL(cr.CrossRefCount, 0) * 1.0 / s.SenseCount) / 4.0
                            END * 0.45
                        ) AS Score
                    FROM dbo.Concept c
                    LEFT JOIN SenseStats s
                        ON s.ConceptId = c.ConceptId
                    LEFT JOIN CrossRefStats cr
                        ON cr.ConceptId = c.ConceptId
                ) AS src
                ON t.ConceptId = src.ConceptId
                WHEN MATCHED THEN
                    UPDATE SET
                        RankScore = src.Score,
                        UpdatedUtc = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (ConceptId, RankScore, UpdatedUtc)
                    VALUES (src.ConceptId, src.Score, SYSUTCDATETIME());
                """,
                cancellationToken: ct,
                commandTimeout: 0));
    }

    private async Task<int> CalculateSenseRank(
        SqlConnection conn,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await conn.ExecuteAsync(
            new CommandDefinition(
                """
                MERGE dbo.SenseRank AS t
                USING
                (
                    SELECT
                        p.DictionaryEntryParsedId,
                        (
                            CASE
                                WHEN COUNT(cr.ToNodeId) / 5.0 > 1.0
                                    THEN 1.0
                                ELSE COUNT(cr.ToNodeId) / 5.0
                            END * 0.40 +

                            ISNULL(c.ConfidenceScore, 0) * 0.35 +

                            CASE
                                WHEN COUNT(DISTINCT e.SourceCode) / 3.0 > 1.0
                                    THEN 1.0
                                ELSE COUNT(DISTINCT e.SourceCode) / 3.0
                            END * 0.25
                        ) AS Score
                    FROM dbo.DictionaryEntryParsed p
                    JOIN dbo.DictionaryEntry e
                        ON e.DictionaryEntryId = p.DictionaryEntryId
                    LEFT JOIN dbo.GraphEdge cr
                        ON cr.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                       AND cr.RelationType IN ('SEE','RELATED_TO','COMPARE')
                    LEFT JOIN dbo.GraphEdge bc
                        ON bc.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                       AND bc.RelationType = 'BELONGS_TO'
                    LEFT JOIN dbo.Concept c
                        ON bc.ToNodeId = CONCAT('Concept:', c.ConceptId)
                    GROUP BY
                        p.DictionaryEntryParsedId,
                        c.ConfidenceScore
                ) AS s
                ON t.DictionaryEntryParsedId = s.DictionaryEntryParsedId
                WHEN MATCHED THEN
                    UPDATE SET
                        RankScore = s.Score,
                        UpdatedUtc = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (DictionaryEntryParsedId, RankScore, UpdatedUtc)
                    VALUES (s.DictionaryEntryParsedId, s.Score, SYSUTCDATETIME());
                """,
                cancellationToken: ct,
                commandTimeout: 0));
    }

    private async Task<int> CalculateWordRank(
        SqlConnection conn,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await conn.ExecuteAsync(
            new CommandDefinition(
                """
                MERGE dbo.WordRank AS t
                USING
                (
                    SELECT
                        e.CanonicalWordId,
                        (
                            ISNULL(MAX(sr.RankScore), 0) +
                            CASE
                                WHEN COUNT(sr.DictionaryEntryParsedId) / 5.0 > 1.0
                                    THEN 1.0
                                ELSE COUNT(sr.DictionaryEntryParsedId) / 5.0
                            END * 0.30
                        ) AS Score
                    FROM dbo.DictionaryEntry e
                    JOIN dbo.DictionaryEntryParsed p
                        ON p.DictionaryEntryId = e.DictionaryEntryId
                    JOIN dbo.SenseRank sr
                        ON sr.DictionaryEntryParsedId = p.DictionaryEntryParsedId
                    WHERE e.CanonicalWordId IS NOT NULL
                    GROUP BY
                        e.CanonicalWordId
                ) AS s
                ON t.CanonicalWordId = s.CanonicalWordId
                WHEN MATCHED THEN
                    UPDATE SET
                        RankScore = s.Score,
                        UpdatedUtc = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (CanonicalWordId, RankScore, UpdatedUtc)
                    VALUES (s.CanonicalWordId, s.Score, SYSUTCDATETIME());
                """,
                cancellationToken: ct,
                commandTimeout: 0));
    }
}