namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryConceptMerger(
        string connectionString,
        ILogger<DictionaryConceptMerger> logger)
    {
        public async Task MergeAsync(
            CancellationToken ct)
        {
            logger.LogInformation(
                "ConceptMerger started");

            await using var conn =
                new SqlConnection(connectionString);

            await conn.OpenAsync(ct);

            var duplicates =
                (await conn.QueryAsync<(string Key, int Count)>(
                    """
                    SELECT ConceptKey, COUNT(*) AS Cnt
                    FROM dbo.Concept
                    GROUP BY ConceptKey
                    HAVING COUNT(*) > 1
                    """))
                .ToList();

            logger.LogInformation(
                "ConceptMerger | DuplicateKeys={Count}",
                duplicates.Count);

            var processedKeys = 0;
            var mergedAliases = 0;

            foreach (var dup in duplicates)
            {
                ct.ThrowIfCancellationRequested();
                processedKeys++;

                if (processedKeys % 100 == 0)
                    logger.LogInformation(
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

            logger.LogInformation(
                "ConceptMerger completed | DuplicateKeys={Keys} | AliasesMerged={Aliases}",
                processedKeys,
                mergedAliases);
        }
    }
}