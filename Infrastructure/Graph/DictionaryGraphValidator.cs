using DictionaryImporter.Domain;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryGraphValidator(
        string connectionString,
        ILogger<DictionaryGraphValidator> logger)
        : IGraphValidator
    {
        public async Task ValidateAsync(
            string sourceCode,
            CancellationToken ct)
        {
            await using var conn =
                new SqlConnection(connectionString);

            await conn.OpenAsync(ct);

            logger.LogInformation(
                "Graph validation started for source {Source}",
                sourceCode);

            await ValidateRelationTypes(conn, ct);
            await ValidateSelfLoops(conn, ct);
            await ValidateOrphanEdges(conn, ct);
            await ValidateSenseHierarchy(conn, ct);

            logger.LogInformation(
                "Graph validation completed for source {Source}",
                sourceCode);
        }

        private async Task ValidateRelationTypes(
            SqlConnection conn,
            CancellationToken ct)
        {
            var invalid =
                await conn.QueryAsync<string>(
                    """
                    SELECT DISTINCT RelationType
                    FROM dbo.GraphEdge
                    WHERE RelationType NOT IN @Allowed
                    """,
                    new { Allowed = GraphRelations.All });

            foreach (var rel in invalid)
                logger.LogError(
                    "Invalid graph relation detected: {RelationType}",
                    rel);

            if (invalid.Any())
                throw new InvalidOperationException(
                    "Graph validation failed: invalid relation types");
        }

        private async Task ValidateSelfLoops(
            SqlConnection conn,
            CancellationToken ct)
        {
            var count =
                await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM dbo.GraphEdge
                    WHERE FromNodeId = ToNodeId
                    """);

            if (count > 0)
                throw new InvalidOperationException(
                    "Graph validation failed: self-loop edges detected");
        }

        private async Task ValidateOrphanEdges(
            SqlConnection conn,
            CancellationToken ct)
        {
            var count =
                await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM dbo.GraphEdge g
                    WHERE NOT EXISTS
                    (
                        SELECT 1 FROM dbo.GraphNode n
                        WHERE n.NodeId = g.FromNodeId
                    )
                    OR NOT EXISTS
                    (
                        SELECT 1 FROM dbo.GraphNode n
                        WHERE n.NodeId = g.ToNodeId
                    )
                    """);

            if (count > 0)
                throw new InvalidOperationException(
                    "Graph validation failed: orphan edges detected");
        }

        private async Task ValidateSenseHierarchy(
            SqlConnection conn,
            CancellationToken ct)
        {
            var count =
                await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM dbo.GraphEdge g
                    WHERE g.RelationType = 'SUB_SENSE_OF'
                    AND NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.GraphEdge parent
                        WHERE parent.ToNodeId = g.FromNodeId
                          AND parent.RelationType = 'HAS_SENSE'
                    )
                    """);

            if (count > 0)
                throw new InvalidOperationException(
                    "Graph validation failed: broken sense hierarchy detected");
        }
    }
}