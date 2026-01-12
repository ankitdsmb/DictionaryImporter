namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryConceptMerger
{
    private readonly string _connectionString;
    private readonly ILogger<DictionaryConceptMerger> _logger;

    public DictionaryConceptMerger(
        string connectionString,
        ILogger<DictionaryConceptMerger> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task MergeAsync(
        CancellationToken ct)
    {
        _logger.LogInformation(
            "ConceptMerger started");

        await using var conn =
            new SqlConnection(_connectionString);

        await conn.OpenAsync(ct);

        // --------------------------------------------------
        // 1. FIND DUPLICATE CONCEPT KEYS
        // --------------------------------------------------
        var duplicates =
            (await conn.QueryAsync<(string Key, int Count)>(
                """
                SELECT ConceptKey, COUNT(*) AS Cnt
                FROM dbo.Concept
                GROUP BY ConceptKey
                HAVING COUNT(*) > 1
                """))
            .ToList();

        _logger.LogInformation(
            "ConceptMerger | DuplicateKeys={Count}",
            duplicates.Count);

        var processedKeys = 0;
        var mergedAliases = 0;

        foreach (var dup in duplicates)
        {
            ct.ThrowIfCancellationRequested();
            processedKeys++;

            // --------------------------------------------------
            // HEARTBEAT (every 100 duplicate keys)
            // --------------------------------------------------
            if (processedKeys % 100 == 0)
                _logger.LogInformation(
                    "ConceptMerger progress | ProcessedKeys={Processed}/{Total}",
                    processedKeys,
                    duplicates.Count);

            var concepts =
                (await conn.QueryAsync<long>(
                    """
                    SELECT ConceptId
                    FROM dbo.Concept
                    WHERE ConceptKey = @Key
                    ORDER BY ConceptId
                    """,
                    new { dup.Key }))
                .ToList();

            var canonicalId = concepts.First();
            var aliases = concepts.Skip(1).ToList();

            // --------------------------------------------------
            // 2. REDIRECT GRAPH EDGES
            // --------------------------------------------------
            foreach (var aliasId in aliases)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.GraphEdge
                    SET ToNodeId = CONCAT('Concept:', @Canonical)
                    WHERE ToNodeId = CONCAT('Concept:', @Alias);
                    """,
                    new
                    {
                        Canonical = canonicalId,
                        Alias = aliasId
                    });

                // ----------------------------------------------
                // 3. RECORD ALIAS
                // ----------------------------------------------
                await conn.ExecuteAsync(
                    """
                    INSERT INTO dbo.ConceptAlias
                    (CanonicalConceptId, AliasConceptKey, SourceCode, CreatedUtc)
                    SELECT
                        @Canonical,
                        ConceptKey,
                        SourceCode,
                        SYSUTCDATETIME()
                    FROM dbo.Concept
                    WHERE ConceptId = @Alias;
                    """,
                    new
                    {
                        Canonical = canonicalId,
                        Alias = aliasId
                    });

                mergedAliases++;
            }
        }

        _logger.LogInformation(
            "ConceptMerger completed | DuplicateKeys={Keys} | AliasesMerged={Aliases}",
            processedKeys,
            mergedAliases);
    }
}