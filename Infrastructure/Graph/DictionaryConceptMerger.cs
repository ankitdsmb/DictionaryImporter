using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryConceptMerger(
    string connectionString,
    ILogger<DictionaryConceptMerger> logger)
{
    private readonly string _connectionString =
        connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly ILogger<DictionaryConceptMerger> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task MergeAsync(
        CancellationToken ct)
    {
        _logger.LogInformation("ConceptMerger started");

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var result =
                await conn.QuerySingleOrDefaultAsync<ConceptMergeResultRow>(
                    new CommandDefinition(
                        "sp_ConceptAlias_RebuildFromConcept",
                        commandType: CommandType.StoredProcedure,
                        cancellationToken: ct));

            if (result is null)
            {
                _logger.LogInformation("ConceptMerger completed | NoResultReturned");
                return;
            }

            _logger.LogInformation(
                "ConceptMerger completed | CanonicalCount={CanonicalCount} | AliasRowsInserted={AliasRowsInserted}",
                result.CanonicalConceptCount,
                result.AliasRowsInserted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // STRICT: never crash pipeline
            _logger.LogError(ex, "ConceptMerger failed (non-fatal)");
        }
    }

    private sealed class ConceptMergeResultRow
    {
        public long CanonicalConceptCount { get; init; }
        public long AliasRowsInserted { get; init; }
    }
}