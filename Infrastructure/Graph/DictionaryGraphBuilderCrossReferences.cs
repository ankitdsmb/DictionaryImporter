using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Graph
{
    internal static class DictionaryGraphBuilderCrossReferences
    {
        public static async Task BuildCrossReferenceEdgesAsync(
            SqlConnection conn,
            string sourceCode,
            CancellationToken ct)
        {
            // ==================================================
            // SEE
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphEdge
                (
                    FromNodeId,
                    ToNodeId,
                    RelationType,
                    CreatedUtc
                )
                SELECT
                    CONCAT('Sense:', cr.SourceParsedId),
                    CONCAT('Sense:', tp.DictionaryEntryParsedId),
                    'SEE',
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryCrossReference cr
                JOIN dbo.CanonicalWord cw
                    ON cw.NormalizedWord = cr.TargetWord
                JOIN dbo.DictionaryEntry de
                    ON de.CanonicalWordId = cw.CanonicalWordId
                   AND de.SourceCode = @SourceCode
                JOIN dbo.DictionaryEntryParsed tp
                    ON tp.DictionaryEntryId = de.DictionaryEntryId
                WHERE cr.ReferenceType = 'See'
                  AND cr.SourceParsedId <> tp.DictionaryEntryParsedId   -- ✅ PREVENT SELF-LOOPS
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', cr.SourceParsedId)
                      AND g.ToNodeId     = CONCAT('Sense:', tp.DictionaryEntryParsedId)
                      AND g.RelationType = 'SEE'
                );
                """,
                new { SourceCode = sourceCode },
                commandTimeout: 0);

            // ==================================================
            // SEE ALSO
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphEdge
                (
                    FromNodeId,
                    ToNodeId,
                    RelationType,
                    CreatedUtc
                )
                SELECT
                    CONCAT('Sense:', cr.SourceParsedId),
                    CONCAT('Sense:', tp.DictionaryEntryParsedId),
                    'RELATED_TO',
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryCrossReference cr
                JOIN dbo.CanonicalWord cw
                    ON cw.NormalizedWord = cr.TargetWord
                JOIN dbo.DictionaryEntry de
                    ON de.CanonicalWordId = cw.CanonicalWordId
                   AND de.SourceCode = @SourceCode
                JOIN dbo.DictionaryEntryParsed tp
                    ON tp.DictionaryEntryId = de.DictionaryEntryId
                WHERE cr.ReferenceType = 'SeeAlso'
                  AND cr.SourceParsedId <> tp.DictionaryEntryParsedId   -- ✅ PREVENT SELF-LOOPS
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', cr.SourceParsedId)
                      AND g.ToNodeId     = CONCAT('Sense:', tp.DictionaryEntryParsedId)
                      AND g.RelationType = 'RELATED_TO'
                );
                """,
                new { SourceCode = sourceCode },
                commandTimeout: 0);

            // ==================================================
            // CF.
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphEdge
                (
                    FromNodeId,
                    ToNodeId,
                    RelationType,
                    CreatedUtc
                )
                SELECT
                    CONCAT('Sense:', cr.SourceParsedId),
                    CONCAT('Sense:', tp.DictionaryEntryParsedId),
                    'COMPARE',
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryCrossReference cr
                JOIN dbo.CanonicalWord cw
                    ON cw.NormalizedWord = cr.TargetWord
                JOIN dbo.DictionaryEntry de
                    ON de.CanonicalWordId = cw.CanonicalWordId
                   AND de.SourceCode = @SourceCode
                JOIN dbo.DictionaryEntryParsed tp
                    ON tp.DictionaryEntryId = de.DictionaryEntryId
                WHERE cr.ReferenceType = 'Cf'
                  AND cr.SourceParsedId <> tp.DictionaryEntryParsedId   -- ✅ PREVENT SELF-LOOPS
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', cr.SourceParsedId)
                      AND g.ToNodeId     = CONCAT('Sense:', tp.DictionaryEntryParsedId)
                      AND g.RelationType = 'COMPARE'
                );
                """,
                new { SourceCode = sourceCode },
                commandTimeout: 0);
        }
    }
}