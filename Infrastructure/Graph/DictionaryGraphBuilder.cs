using Dapper;
using DictionaryImporter.Core.Graph;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryGraphBuilder : IGraphBuilder
    {
        private readonly string _connectionString;

        public DictionaryGraphBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task BuildAsync(
            string sourceCode,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // ==================================================
            // WORD → SENSE
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, CreatedUtc)
                SELECT
                    CONCAT('Word:', e.CanonicalWordId),
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    'HAS_SENSE',
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
                new { SourceCode = sourceCode });

            // ==================================================
            // SENSE → SENSE (HIERARCHY)
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, CreatedUtc)
                SELECT
                    CONCAT('Sense:', p.ParentParsedId),
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    'SUB_SENSE_OF',
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryParsed p
                WHERE p.ParentParsedId IS NOT NULL
                  AND p.ParentParsedId <> p.DictionaryEntryParsedId
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', p.ParentParsedId)
                      AND g.ToNodeId     = CONCAT('Sense:', p.DictionaryEntryParsedId)
                      AND g.RelationType = 'SUB_SENSE_OF'
                );
                """);

            // ==================================================
            // SENSE → DOMAIN
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, CreatedUtc)
                SELECT
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    CONCAT('Domain:', LTRIM(RTRIM(p.Domain))),
                    'IN_DOMAIN',
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryParsed p
                WHERE p.Domain IS NOT NULL
                  AND LTRIM(RTRIM(p.Domain)) <> ''
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', p.DictionaryEntryParsedId)
                      AND g.ToNodeId     = CONCAT('Domain:', LTRIM(RTRIM(p.Domain)))
                      AND g.RelationType = 'IN_DOMAIN'
                );
                """);

            // ==================================================
            // SENSE → LANGUAGE (ETYMOLOGY)
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphEdge
                (FromNodeId, ToNodeId, RelationType, CreatedUtc)
                SELECT
                    CONCAT('Sense:', p.DictionaryEntryParsedId),
                    CONCAT('Lang:', LTRIM(RTRIM(e.LanguageCode))),
                    'DERIVED_FROM',
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryEtymology e
                JOIN dbo.DictionaryEntryParsed p
                    ON p.DictionaryEntryId = e.DictionaryEntryId
                WHERE e.LanguageCode IS NOT NULL
                  AND LTRIM(RTRIM(e.LanguageCode)) <> ''
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.GraphEdge g
                    WHERE g.FromNodeId   = CONCAT('Sense:', p.DictionaryEntryParsedId)
                      AND g.ToNodeId     = CONCAT('Lang:', LTRIM(RTRIM(e.LanguageCode)))
                      AND g.RelationType = 'DERIVED_FROM'
                );
                """);

            // ==================================================
            // CROSS REFERENCES (SEE / SEE ALSO / CF)
            // ==================================================
            await DictionaryGraphBuilderCrossReferences
                .BuildCrossReferenceEdgesAsync(conn, sourceCode, ct);
        }
    }
}
