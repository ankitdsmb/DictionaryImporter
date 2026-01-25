using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace DictionaryImporter.Infrastructure.Graph;

internal static class DictionaryGraphBuilderCrossReferences
{
    public static async Task BuildAsync(
        SqlConnection conn,
        string sourceCode,
        GraphRebuildMode rebuildMode,
        CancellationToken ct,
        ILogger? logger = null)
    {
        logger?.LogInformation(
            "GraphCrossRef started | Source={Source} | Mode={Mode}",
            sourceCode,
            rebuildMode);

        if (conn is null)
            return;

        if (conn.State != System.Data.ConnectionState.Open)
            return;

        sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

        try
        {
            var inserted =
                await conn.ExecuteScalarAsync<long>(
                    new CommandDefinition(
                        "sp_GraphEdge_BuildCrossReferencesBySource",
                        new
                        {
                            SourceCode = sourceCode,
                            RebuildMode = rebuildMode.ToString()
                        },
                        commandType: CommandType.StoredProcedure,
                        cancellationToken: ct,
                        commandTimeout: 0));

            logger?.LogInformation(
                "GraphCrossRef completed | Source={Source} | InsertedEdges={Inserted}",
                sourceCode,
                inserted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // STRICT: never crash graph building
            logger?.LogWarning(
                ex,
                "GraphCrossRef failed (non-fatal) | Source={Source}",
                sourceCode);
        }
    }
}