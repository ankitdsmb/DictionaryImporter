using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Graph;

public sealed class DictionaryConceptConfidenceCalculator(
    string connectionString,
    ILogger<DictionaryConceptConfidenceCalculator> logger)
{
    private readonly string _connectionString =
        connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly ILogger<DictionaryConceptConfidenceCalculator> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task CalculateAsync(
        CancellationToken ct)
    {
        _logger.LogInformation("ConceptConfidence calculation started");

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var updated =
                await conn.ExecuteScalarAsync<long>(
                    new CommandDefinition(
                        "sp_ConceptConfidence_RecalculateAll",
                        commandType: CommandType.StoredProcedure,
                        cancellationToken: ct));

            _logger.LogInformation(
                "ConceptConfidence calculation completed | Updated={Count}",
                updated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // STRICT: never crash pipeline
            _logger.LogError(ex, "ConceptConfidence calculation failed (non-fatal)");
        }
    }
}