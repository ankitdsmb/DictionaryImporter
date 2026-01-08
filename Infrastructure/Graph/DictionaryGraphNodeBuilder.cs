using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryGraphNodeBuilder
    {
        private readonly string _connectionString;

        public DictionaryGraphNodeBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task BuildAsync(string sourceCode, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // ==================================================
            // WORD NODES
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphNode (NodeId, NodeType, RefId, CreatedUtc)
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
                    SELECT 1 FROM dbo.GraphNode n
                    WHERE n.NodeId = CONCAT('Word:', CanonicalWordId)
                );
                """,
                new { SourceCode = sourceCode });

            // ==================================================
            // SENSE NODES
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphNode (NodeId, NodeType, RefId, CreatedUtc)
                SELECT DISTINCT
                    CONCAT('Sense:', DictionaryEntryParsedId),
                    'Sense',
                    DictionaryEntryParsedId,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryParsed
                WHERE NOT EXISTS
                (
                    SELECT 1 FROM dbo.GraphNode n
                    WHERE n.NodeId = CONCAT('Sense:', DictionaryEntryParsedId)
                );
                """);

            // ==================================================
            // DOMAIN NODES
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphNode (NodeId, NodeType, RefId, CreatedUtc)
                SELECT DISTINCT
                    CONCAT('Domain:', Domain),
                    'Domain',
                    NULL,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryParsed
                WHERE Domain IS NOT NULL
                  AND NOT EXISTS
                (
                    SELECT 1 FROM dbo.GraphNode n
                    WHERE n.NodeId = CONCAT('Domain:', Domain)
                );
                """);

            // ==================================================
            // LANGUAGE NODES
            // ==================================================
            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.GraphNode (NodeId, NodeType, RefId, CreatedUtc)
                SELECT DISTINCT
                    CONCAT('Lang:', LanguageCode),
                    'Language',
                    NULL,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntryEtymology
                WHERE LanguageCode IS NOT NULL
                  AND NOT EXISTS
                (
                    SELECT 1 FROM dbo.GraphNode n
                    WHERE n.NodeId = CONCAT('Lang:', LanguageCode)
                );
                """);
        }
    }
}