namespace DictionaryImporter.Infrastructure.Graph;

internal static class DictionaryGraphBuilderCrossReferences
{
    public static async Task BuildAsync(
        SqlConnection conn,
        string sourceCode,
        GraphRebuildMode rebuildMode,
        CancellationToken ct,
        ILogger? logger = null)
    {
        logger?.LogInformation(
            "GraphCrossRef started | Source={Source} | Mode={Mode}",
            sourceCode,
            rebuildMode);

        if (rebuildMode == GraphRebuildMode.Rebuild)
        {
            var deleted = await DeleteExistingEdgesAsync(conn, sourceCode, ct);

            logger?.LogInformation(
                "GraphCrossRef rebuild | Source={Source} | DeletedEdges={Count}",
                sourceCode,
                deleted);
        }

        await InsertEdgesAsync(
            conn,
            sourceCode,
            "See",
            "SEE",
            1.0m,
            false,
            ct,
            logger);

        await InsertEdgesAsync(
            conn,
            sourceCode,
            "SeeAlso",
            "RELATED_TO",
            0.8m,
            true,
            ct,
            logger);

        await InsertEdgesAsync(
            conn,
            sourceCode,
            "Cf",
            "COMPARE",
            0.7m,
            true,
            ct,
            logger);

        logger?.LogInformation(
            "GraphCrossRef completed | Source={Source}",
            sourceCode);
    }

    private static async Task InsertEdgesAsync(
        SqlConnection conn,
        string sourceCode,
        string referenceType,
        string relationType,
        decimal confidence,
        bool addReverseEdge,
        CancellationToken ct,
        ILogger? logger)
    {
        var inserted =
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO dbo.GraphEdge
                    (
                        FromNodeId,
                        ToNodeId,
                        RelationType,
                        SourceCode,
                        Confidence,
                        CreatedUtc
                    )
                    SELECT DISTINCT
                        src.FromNodeId,
                        src.ToNodeId,
                        @RelationType,
                        @SourceCode,
                        @Confidence,
                        SYSUTCDATETIME()
                    FROM
                    (
                        SELECT
                            CONCAT('Sense:', cr.SourceParsedId)          AS FromNodeId,
                            CONCAT('Sense:', tp.DictionaryEntryParsedId) AS ToNodeId
                        FROM dbo.DictionaryEntryCrossReference cr
                        JOIN dbo.CanonicalWord cw
                            ON cw.NormalizedWord = cr.TargetWord
                        JOIN dbo.DictionaryEntry de
                            ON de.CanonicalWordId = cw.CanonicalWordId
                           AND de.SourceCode = @SourceCode
                        JOIN dbo.DictionaryEntryParsed tp
                            ON tp.DictionaryEntryId = de.DictionaryEntryId
                        WHERE cr.ReferenceType = @ReferenceType
                          AND cr.SourceParsedId <> tp.DictionaryEntryParsedId
                    ) src
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphEdge g
                        WHERE g.FromNodeId   = src.FromNodeId
                          AND g.ToNodeId     = src.ToNodeId
                          AND g.RelationType = @RelationType
                    );
                    """,
                    new
                    {
                        SourceCode = sourceCode,
                        ReferenceType = referenceType,
                        RelationType = relationType,
                        Confidence = confidence
                    },
                    cancellationToken: ct,
                    commandTimeout: 0));

        logger?.LogInformation(
            "GraphCrossRef | Source={Source} | Relation={Relation} | Direction=Forward | Inserted={Count}",
            sourceCode,
            relationType,
            inserted);

        if (addReverseEdge)
        {
            var reverseInserted =
                await InsertReverseEdgesAsync(
                    conn,
                    sourceCode,
                    relationType,
                    confidence,
                    ct);

            logger?.LogInformation(
                "GraphCrossRef | Source={Source} | Relation={Relation} | Direction=Reverse | Inserted={Count}",
                sourceCode,
                relationType,
                reverseInserted);
        }
    }

    private static async Task<int> InsertReverseEdgesAsync(
        SqlConnection conn,
        string sourceCode,
        string relationType,
        decimal confidence,
        CancellationToken ct)
    {
        return await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO dbo.GraphEdge
                (
                    FromNodeId,
                    ToNodeId,
                    RelationType,
                    SourceCode,
                    Confidence,
                    CreatedUtc
                )
                SELECT
                    g.ToNodeId,
                    g.FromNodeId,
                    g.RelationType,
                    g.SourceCode,
                    g.Confidence,
                    SYSUTCDATETIME()
                FROM dbo.GraphEdge g
                WHERE g.SourceCode = @SourceCode
                  AND g.RelationType = @RelationType
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.GraphEdge r
                      WHERE r.FromNodeId = g.ToNodeId
                        AND r.ToNodeId = g.FromNodeId
                        AND r.RelationType = g.RelationType
                  );
                """,
                new
                {
                    SourceCode = sourceCode,
                    RelationType = relationType
                },
                cancellationToken: ct,
                commandTimeout: 0));
    }

    private static async Task<int> DeleteExistingEdgesAsync(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        return await conn.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM dbo.GraphEdge
                WHERE SourceCode = @SourceCode
                  AND RelationType IN ('SEE', 'RELATED_TO', 'COMPARE');
                """,
                new { SourceCode = sourceCode },
                cancellationToken: ct));
    }
}