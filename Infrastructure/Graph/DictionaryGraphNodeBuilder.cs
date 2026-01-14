namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryGraphNodeBuilder(
    string connectionString,
    ILogger<DictionaryGraphNodeBuilder> logger)
{
    public async Task BuildAsync(
        string sourceCode,
        CancellationToken ct)
    {
        logger.LogInformation(
            "GraphNodeBuilder started | Source={Source}",
            sourceCode);

        var sw = Stopwatch.StartNew();

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var wordNodes =
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO dbo.GraphNode
                        (NodeId, NodeType, RefId, CreatedUtc)
                    SELECT DISTINCT
                        CONCAT('Word:', CanonicalWordId),
                        'Word',
                        CanonicalWordId,
                        SYSUTCDATETIME()
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode
                      AND CanonicalWordId IS NOT NULL
                      AND NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphNode n
                        WHERE n.NodeId = CONCAT('Word:', CanonicalWordId)
                    );
                    """,
                    new { SourceCode = sourceCode },
                    cancellationToken: ct,
                    commandTimeout: 0));

        logger.LogInformation(
            "GraphNodeBuilder | Source={Source} | NodeType=Word | Inserted={Count}",
            sourceCode,
            wordNodes);

        var senseNodes =
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO dbo.GraphNode
                        (NodeId, NodeType, RefId, CreatedUtc)
                    SELECT DISTINCT
                        CONCAT('Sense:', DictionaryEntryParsedId),
                        'Sense',
                        DictionaryEntryParsedId,
                        SYSUTCDATETIME()
                    FROM dbo.DictionaryEntryParsed
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphNode n
                        WHERE n.NodeId = CONCAT('Sense:', DictionaryEntryParsedId)
                    );
                    """,
                    cancellationToken: ct,
                    commandTimeout: 0));

        logger.LogInformation(
            "GraphNodeBuilder | Source={Source} | NodeType=Sense | Inserted={Count}",
            sourceCode,
            senseNodes);

        var domainNodes =
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO dbo.GraphNode
                        (NodeId, NodeType, RefId, CreatedUtc)
                    SELECT DISTINCT
                        CONCAT('Domain:', LTRIM(RTRIM(Domain))),
                        'Domain',
                        NULL,
                        SYSUTCDATETIME()
                    FROM dbo.DictionaryEntryParsed
                    WHERE Domain IS NOT NULL
                      AND LTRIM(RTRIM(Domain)) <> ''
                      AND NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphNode n
                        WHERE n.NodeId = CONCAT('Domain:', LTRIM(RTRIM(Domain)))
                    );
                    """,
                    cancellationToken: ct,
                    commandTimeout: 0));

        logger.LogInformation(
            "GraphNodeBuilder | Source={Source} | NodeType=Domain | Inserted={Count}",
            sourceCode,
            domainNodes);

        var languageNodes =
            await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO dbo.GraphNode
                        (NodeId, NodeType, RefId, CreatedUtc)
                    SELECT DISTINCT
                        CONCAT('Lang:', LTRIM(RTRIM(LanguageCode))),
                        'Language',
                        NULL,
                        SYSUTCDATETIME()
                    FROM dbo.DictionaryEntryEtymology
                    WHERE LanguageCode IS NOT NULL
                      AND LTRIM(RTRIM(LanguageCode)) <> ''
                      AND NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphNode n
                        WHERE n.NodeId = CONCAT('Lang:', LTRIM(RTRIM(LanguageCode)))
                    );
                    """,
                    cancellationToken: ct,
                    commandTimeout: 0));

        logger.LogInformation(
            "GraphNodeBuilder | Source={Source} | NodeType=Language | Inserted={Count}",
            sourceCode,
            languageNodes);

        sw.Stop();

        logger.LogInformation(
            "GraphNodeBuilder completed | Source={Source} | DurationMs={Duration}",
            sourceCode,
            sw.ElapsedMilliseconds);
    }
}