using DictionaryImporter.Core.Graph;

namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryGraphValidator : IGraphValidator
{
    private readonly string _connectionString;
    private readonly ILogger<DictionaryGraphValidator> _logger;

    public DictionaryGraphValidator(
        string connectionString,
        ILogger<DictionaryGraphValidator> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task ValidateAsync(
        string sourceCode,
        CancellationToken ct)
    {
        await using var conn =
            new SqlConnection(_connectionString);

        await conn.OpenAsync(ct);

        _logger.LogInformation(
            "Graph validation started for source {Source}",
            sourceCode);

        await ValidateRelationTypes(conn, ct);
        await ValidateSelfLoops(conn, ct);
        await ValidateOrphanEdges(conn, ct);
        await ValidateSenseHierarchy(conn, ct);

        _logger.LogInformation(
            "Graph validation completed for source {Source}",
            sourceCode);
    }

    // --------------------------------------------------
    // 1. INVALID RELATION TYPES
    // --------------------------------------------------
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
            _logger.LogError(
                "Invalid graph relation detected: {RelationType}",
                rel);

        if (invalid.Any())
            throw new InvalidOperationException(
                "Graph validation failed: invalid relation types");
    }

    // --------------------------------------------------
    // 2. SELF-LOOPS (Sense → Same Sense)
    // --------------------------------------------------
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

    // --------------------------------------------------
    // 3. ORPHAN EDGES
    // --------------------------------------------------
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

    // --------------------------------------------------
    // 4. BROKEN SENSE HIERARCHY
    // --------------------------------------------------
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