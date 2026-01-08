using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryConceptMerger
    {
        private readonly string _connectionString;

        public DictionaryConceptMerger(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task MergeAsync(
            CancellationToken ct)
        {
            await using var conn =
                new SqlConnection(_connectionString);

            await conn.OpenAsync(ct);

            // --------------------------------------------------
            // 1. FIND DUPLICATE CONCEPT KEYS
            // --------------------------------------------------
            var duplicates =
                await conn.QueryAsync<(string Key, int Count)>(
                    """
                    SELECT ConceptKey, COUNT(*) AS Cnt
                    FROM dbo.Concept
                    GROUP BY ConceptKey
                    HAVING COUNT(*) > 1
                    """);

            foreach (var dup in duplicates)
            {
                ct.ThrowIfCancellationRequested();

                var concepts =
                    (await conn.QueryAsync<long>(
                        """
                        SELECT ConceptId
                        FROM dbo.Concept
                        WHERE ConceptKey = @Key
                        ORDER BY ConceptId
                        """,
                        new { Key = dup.Key }))
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
                }
            }
        }
    }
}