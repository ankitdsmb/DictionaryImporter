using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryGraphValidator(
        string connectionString,
        ILogger<DictionaryGraphValidator> logger)
        : IGraphValidator
    {
        private readonly string _connectionString =
            connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private readonly ILogger<DictionaryGraphValidator> _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task ValidateAsync(
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

            _logger.LogInformation(
                "Graph validation started for source {Source}",
                sourceCode);

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var result =
                    await conn.QuerySingleOrDefaultAsync<GraphValidationSummaryRow>(
                        new CommandDefinition(
                            "sp_Graph_ValidateBySource",
                            new { SourceCode = sourceCode },
                            commandType: CommandType.StoredProcedure,
                            cancellationToken: ct,
                            commandTimeout: 0));

                var invalidRelations =
                    (await conn.QueryAsync<InvalidRelationRow>(
                        new CommandDefinition(
                            "sp_Graph_ValidateInvalidRelationsBySource",
                            new { SourceCode = sourceCode },
                            commandType: CommandType.StoredProcedure,
                            cancellationToken: ct,
                            commandTimeout: 0)))
                    .ToList();

                if (invalidRelations.Count > 0)
                {
                    foreach (var rel in invalidRelations)
                    {
                        _logger.LogError(
                            "Invalid graph relation detected | Source={Source} | RelationType={RelationType}",
                            sourceCode,
                            rel.RelationType);
                    }
                }

                if (result is null)
                {
                    _logger.LogWarning(
                        "Graph validation returned no results | Source={Source}",
                        sourceCode);
                    return;
                }

                _logger.LogInformation(
                    "Graph validation summary | Source={Source} | SelfLoops={SelfLoops} | OrphanEdges={OrphanEdges} | BrokenSenseHierarchy={BrokenHierarchy} | InvalidRelationTypes={InvalidRelations}",
                    sourceCode,
                    result.SelfLoopCount,
                    result.OrphanEdgeCount,
                    result.BrokenSenseHierarchyCount,
                    invalidRelations.Count);

                if (result.SelfLoopCount > 0 ||
                    result.OrphanEdgeCount > 0 ||
                    result.BrokenSenseHierarchyCount > 0 ||
                    invalidRelations.Count > 0)
                {
                    // STRICT: log only, never throw
                    _logger.LogError(
                        "Graph validation FAILED (non-fatal) | Source={Source}",
                        sourceCode);
                }
                else
                {
                    _logger.LogInformation(
                        "Graph validation PASSED | Source={Source}",
                        sourceCode);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // STRICT: never crash pipeline
                _logger.LogError(
                    ex,
                    "Graph validation failed (non-fatal) | Source={Source}",
                    sourceCode);
            }

            _logger.LogInformation(
                "Graph validation completed for source {Source}",
                sourceCode);
        }

        private sealed class GraphValidationSummaryRow
        {
            public int SelfLoopCount { get; init; }
            public int OrphanEdgeCount { get; init; }
            public int BrokenSenseHierarchyCount { get; init; }
        }

        private sealed class InvalidRelationRow
        {
            public string RelationType { get; init; } = string.Empty;
        }
    }
}
