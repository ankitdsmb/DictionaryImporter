using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryGraphBuilder(
    string connectionString,
    DictionaryConceptBuilder conceptBuilder,
    DictionaryConceptConfidenceCalculator conceptConfidenceCalculator,
    DictionaryConceptMerger conceptMerger,
    ILogger<DictionaryGraphBuilder> logger)
    : IGraphBuilder
{
    private readonly string _connectionString =
        connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly DictionaryConceptBuilder _conceptBuilder =
        conceptBuilder ?? throw new ArgumentNullException(nameof(conceptBuilder));

    private readonly DictionaryConceptConfidenceCalculator _conceptConfidenceCalculator =
        conceptConfidenceCalculator ?? throw new ArgumentNullException(nameof(conceptConfidenceCalculator));

    private readonly DictionaryConceptMerger _conceptMerger =
        conceptMerger ?? throw new ArgumentNullException(nameof(conceptMerger));

    private readonly ILogger<DictionaryGraphBuilder> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public Task BuildAsync(
        string sourceCode,
        CancellationToken ct)
    {
        return BuildAsync(
            sourceCode,
            GraphRebuildMode.Append,
            ct);
    }

    public async Task BuildAsync(
        string sourceCode,
        GraphRebuildMode rebuildMode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        _logger.LogInformation(
            "GraphBuilder started | Source={Source} | Mode={Mode}",
            sourceCode,
            rebuildMode);

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // ✅ ONE SP for ALL edges (core + crossrefs)
            var insertedEdges =
                await conn.ExecuteScalarAsync<long>(
                    new CommandDefinition(
                        "sp_GraphEdge_BuildAllBySource",
                        new
                        {
                            SourceCode = sourceCode,
                            RebuildMode = rebuildMode.ToString()
                        },
                        commandType: CommandType.StoredProcedure,
                        cancellationToken: ct,
                        commandTimeout: 0));

            _logger.LogInformation(
                "GraphBuilder edges completed | Source={Source} | InsertedEdges={Inserted}",
                sourceCode,
                insertedEdges);

            // Concepts + confidence + merge
            _logger.LogInformation(
                "GraphBuilder | CONCEPTS started | Source={Source}",
                sourceCode);

            try
            {
                await _conceptBuilder.BuildAsync(sourceCode, ct);
                await _conceptConfidenceCalculator.CalculateAsync(ct);
                await _conceptMerger.MergeAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "GraphBuilder | CONCEPTS failed (non-fatal) | Source={Source}",
                    sourceCode);
            }

            _logger.LogInformation(
                "GraphBuilder | CONCEPTS completed | Source={Source}",
                sourceCode);

            _logger.LogInformation(
                "GraphBuilder completed | Source={Source} | Mode={Mode}",
                sourceCode,
                rebuildMode);
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
                "GraphBuilder failed (non-fatal) | Source={Source} | Mode={Mode}",
                sourceCode,
                rebuildMode);
        }
    }
}