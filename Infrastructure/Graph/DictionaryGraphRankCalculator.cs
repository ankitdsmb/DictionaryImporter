using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryGraphRankCalculator
    {
        private readonly string _cs;

        public DictionaryGraphRankCalculator(string connectionString)
        {
            _cs = connectionString;
        }

        public async Task CalculateAsync(CancellationToken ct)
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync(ct);

            await CalculateConceptRank(conn, ct);
            await CalculateSenseRank(conn, ct);
            await CalculateWordRank(conn, ct);
        }

        // --------------------------------------------------
        // CONCEPT RANK
        // --------------------------------------------------
        private async Task CalculateConceptRank(
    SqlConnection conn,
    CancellationToken ct)
        {
            await conn.ExecuteAsync(
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
                    -- Sense volume (capped)
                    CASE
                        WHEN ISNULL(s.SenseCount, 0) / 5.0 > 1.0
                            THEN 1.0
                        ELSE ISNULL(s.SenseCount, 0) / 5.0
                    END * 0.55 +

                    -- Cross-reference density (normalized per sense)
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
        """);
        }

        // --------------------------------------------------
        // SENSE RANK
        // --------------------------------------------------
        private async Task CalculateSenseRank(
    SqlConnection conn,
    CancellationToken ct)
        {
            await conn.ExecuteAsync(
                """
        MERGE dbo.SenseRank AS t
        USING
        (
            SELECT
                p.DictionaryEntryParsedId,
                (
                    -- Cross-reference influence
                    CASE
                        WHEN COUNT(cr.ToNodeId) / 5.0 > 1.0
                            THEN 1.0
                        ELSE COUNT(cr.ToNodeId) / 5.0
                    END * 0.40 +

                    -- Concept confidence (if linked)
                    ISNULL(c.ConfidenceScore, 0) * 0.35 +

                    -- Source diversity
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
        """);
        }

        // --------------------------------------------------
        // WORD RANK
        // --------------------------------------------------
        private async Task CalculateWordRank(
    SqlConnection conn,
    CancellationToken ct)
        {
            await conn.ExecuteAsync(
                """
        MERGE dbo.WordRank AS t
        USING
        (
            SELECT
                e.CanonicalWordId,
                (
                    -- Strongest sense rank
                    ISNULL(MAX(sr.RankScore), 0) +

                    -- Sense count contribution (capped)
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
        """);
        }
    }
}