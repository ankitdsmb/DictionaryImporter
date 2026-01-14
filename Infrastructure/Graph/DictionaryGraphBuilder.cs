using DictionaryImporter.Core.Graph;

namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryGraphBuilder(
    string connectionString,
    ILogger<DictionaryGraphBuilder> logger)
    : IGraphBuilder
{
    public Task BuildAsync(
        string sourceCode,
        CancellationToken ct)
    {
        return BuildAsync(
            sourceCode,
            GraphRebuildMode.Append,
            ct);
    }

    public async Task BuildAsync(
        string sourceCode,
        GraphRebuildMode rebuildMode,
        CancellationToken ct)
    {
        logger.LogInformation(
            "GraphBuilder started | Source={Source} | Mode={Mode}",
            sourceCode,
            rebuildMode);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        logger.LogInformation(
            "GraphBuilder | WORD→SENSE started | Source={Source}",
            sourceCode);

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, SourceCode, Confidence, CreatedUtc)
                SELECT DISTINCT
                    CONCAT('Word:', e.CanonicalWordId),
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    'HAS_SENSE',
                    @SourceCode,
                    1.0,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntry e
                JOIN dbo.DictionaryEntryParsed p
                    ON p.DictionaryEntryId = e.DictionaryEntryId
                WHERE e.SourceCode = @SourceCode
                  AND e.CanonicalWordId IS NOT NULL
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Word:', e.CanonicalWordId)
                      AND g.ToNodeId     = CONCAT('Sense:', p.DictionaryEntryParsedId)
                      AND g.RelationType = 'HAS_SENSE'
                );
                """,
                new { SourceCode = sourceCode },
                cancellationToken: ct,
                commandTimeout: 0));

        logger.LogInformation(
            "GraphBuilder | WORD→SENSE completed | Source={Source}",
            sourceCode);

        logger.LogInformation(
            "GraphBuilder | SENSE→SENSE started | Source={Source}",
            sourceCode);

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, SourceCode, Confidence, CreatedUtc)
                SELECT DISTINCT
                    CONCAT('Sense:', p.ParentParsedId),
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    'SUB_SENSE_OF',
                    @SourceCode,
                    1.0,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryParsed p
                JOIN dbo.DictionaryEntry e
                    ON e.DictionaryEntryId = p.DictionaryEntryId
                WHERE e.SourceCode = @SourceCode
                  AND p.ParentParsedId IS NOT NULL
                  AND p.ParentParsedId <> p.DictionaryEntryParsedId
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', p.ParentParsedId)
                      AND g.ToNodeId     = CONCAT('Sense:', p.DictionaryEntryParsedId)
                      AND g.RelationType = 'SUB_SENSE_OF'
                );
                """,
                new { SourceCode = sourceCode },
                cancellationToken: ct,
                commandTimeout: 0));

        logger.LogInformation(
            "GraphBuilder | SENSE→SENSE completed | Source={Source}",
            sourceCode);

        logger.LogInformation(
            "GraphBuilder | SENSE→DOMAIN started | Source={Source}",
            sourceCode);

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, SourceCode, Confidence, CreatedUtc)
                SELECT DISTINCT
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    CONCAT('Domain:', LTRIM(RTRIM(p.Domain))),
                    'IN_DOMAIN',
                    @SourceCode,
                    0.9,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryParsed p
                JOIN dbo.DictionaryEntry e
                    ON e.DictionaryEntryId = p.DictionaryEntryId
                WHERE e.SourceCode = @SourceCode
                  AND p.Domain IS NOT NULL
                  AND LTRIM(RTRIM(p.Domain)) <> ''
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', p.DictionaryEntryParsedId)
                      AND g.ToNodeId     = CONCAT('Domain:', LTRIM(RTRIM(p.Domain)))
                      AND g.RelationType = 'IN_DOMAIN'
                );
                """,
                new { SourceCode = sourceCode },
                cancellationToken: ct,
                commandTimeout: 0));

        logger.LogInformation(
            "GraphBuilder | SENSE→DOMAIN completed | Source={Source}",
            sourceCode);

        logger.LogInformation(
            "GraphBuilder | SENSE→LANGUAGE started | Source={Source}",
            sourceCode);

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, SourceCode, Confidence, CreatedUtc)
                SELECT DISTINCT
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    CONCAT('Lang:', LTRIM(RTRIM(e.LanguageCode))),
                    'DERIVED_FROM',
                    @SourceCode,
                    0.8,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryEtymology e
                JOIN dbo.DictionaryEntryParsed p
                    ON p.DictionaryEntryId = e.DictionaryEntryId
                JOIN dbo.DictionaryEntry de
                    ON de.DictionaryEntryId = p.DictionaryEntryId
                WHERE de.SourceCode = @SourceCode
                  AND e.LanguageCode IS NOT NULL
                  AND LTRIM(RTRIM(e.LanguageCode)) <> ''
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', p.DictionaryEntryParsedId)
                      AND g.ToNodeId     = CONCAT('Lang:', LTRIM(RTRIM(e.LanguageCode)))
                      AND g.RelationType = 'DERIVED_FROM'
                );
                """,
                new { SourceCode = sourceCode },
                cancellationToken: ct,
                commandTimeout: 0));

        logger.LogInformation(
            "GraphBuilder | SENSE→LANGUAGE completed | Source={Source}",
            sourceCode);

        logger.LogInformation(
            "GraphBuilder | CROSS-REFERENCES started | Source={Source}",
            sourceCode);

        await DictionaryGraphBuilderCrossReferences.BuildAsync(
            conn,
            sourceCode,
            rebuildMode,
            ct);

        logger.LogInformation(
            "GraphBuilder | CROSS-REFERENCES completed | Source={Source}",
            sourceCode);

        logger.LogInformation(
            "GraphBuilder completed | Source={Source} | Mode={Mode}",
            sourceCode,
            rebuildMode);
    }
}