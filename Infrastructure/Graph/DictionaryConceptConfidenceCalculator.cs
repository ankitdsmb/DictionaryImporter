using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryConceptConfidenceCalculator
    {
        private readonly string _connectionString;

        public DictionaryConceptConfidenceCalculator(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task CalculateAsync(
            CancellationToken ct)
        {
            await using var conn =
                new SqlConnection(_connectionString);

            await conn.OpenAsync(ct);

            var concepts =
                await conn.QueryAsync<long>(
                    "SELECT ConceptId FROM dbo.Concept");

            foreach (var conceptId in concepts)
            {
                ct.ThrowIfCancellationRequested();

                // ---------------------------------------------
                // A. Sense count
                // ---------------------------------------------
                var senseCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*)
                        FROM dbo.GraphEdge
                        WHERE ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreA =
                    Math.Min(senseCount / 5.0, 1.0);

                // ---------------------------------------------
                // B. Source count
                // ---------------------------------------------
                var sourceCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(DISTINCT e.SourceCode)
                        FROM dbo.GraphEdge g
                        JOIN dbo.DictionaryEntryParsed p
                            ON g.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                        JOIN dbo.DictionaryEntry e
                            ON e.DictionaryEntryId = p.DictionaryEntryId
                        WHERE g.ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreB =
                    Math.Min(sourceCount / 3.0, 1.0);

                // ---------------------------------------------
                // C. Cross-reference count
                // ---------------------------------------------
                var crossRefCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*)
                        FROM dbo.GraphEdge
                        WHERE FromNodeId IN (
                            SELECT FromNodeId
                            FROM dbo.GraphEdge
                            WHERE ToNodeId = CONCAT('Concept:', @ConceptId)
                              AND RelationType = 'BELONGS_TO'
                        )
                        AND RelationType IN ('SEE', 'RELATED_TO', 'COMPARE')
                        """,
                        new { ConceptId = conceptId });

                var scoreC =
                    Math.Min(crossRefCount / 5.0, 1.0);

                // ---------------------------------------------
                // D. Domain stability
                // ---------------------------------------------
                var domainCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(DISTINCT p.Domain)
                        FROM dbo.GraphEdge g
                        JOIN dbo.DictionaryEntryParsed p
                            ON g.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                        WHERE g.ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreD =
                    domainCount <= 1 ? 1.0 : 0.5;

                // ---------------------------------------------
                // E. POS stability
                // ---------------------------------------------
                var posCount =
                    await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(DISTINCT e.PartOfSpeech)
                        FROM dbo.GraphEdge g
                        JOIN dbo.DictionaryEntryParsed p
                            ON g.FromNodeId = CONCAT('Sense:', p.DictionaryEntryParsedId)
                        JOIN dbo.DictionaryEntry e
                            ON e.DictionaryEntryId = p.DictionaryEntryId
                        WHERE g.ToNodeId = CONCAT('Concept:', @ConceptId)
                          AND g.RelationType = 'BELONGS_TO'
                        """,
                        new { ConceptId = conceptId });

                var scoreE =
                    posCount <= 1 ? 1.0 : 0.0;

                // ---------------------------------------------
                // FINAL SCORE
                // ---------------------------------------------
                var confidence =
                    (scoreA * 0.30) +
                    (scoreB * 0.25) +
                    (scoreC * 0.20) +
                    (scoreD * 0.15) +
                    (scoreE * 0.10);

                await conn.ExecuteAsync(
                    """
                    UPDATE dbo.Concept
                    SET ConfidenceScore = @Score
                    WHERE ConceptId = @ConceptId
                    """,
                    new
                    {
                        ConceptId = conceptId,
                        Score = confidence
                    });
            }
        }
    }
}