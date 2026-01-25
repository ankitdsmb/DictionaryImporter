using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryConceptBuilder(
        string connectionString,
        ILogger<DictionaryConceptBuilder> logger)
    {
        private readonly string _connectionString =
            connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private readonly ILogger<DictionaryConceptBuilder> _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task BuildAsync(
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            _logger.LogInformation(
                "ConceptBuilder started | Source={Source}",
                sourceCode);

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                // 1) Build Concepts + Nodes + Edges in one SP call (fast + safe)
                var affected =
                    await conn.ExecuteScalarAsync<long>(
                        new CommandDefinition(
                            "sp_GraphConcept_BuildFromDictionaryEntryParsed_BySource",
                            new { SourceCode = sourceCode },
                            commandType: CommandType.StoredProcedure,
                            cancellationToken: ct));

                _logger.LogInformation(
                    "ConceptBuilder completed | Source={Source} | Processed={Affected}",
                    sourceCode,
                    affected);
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
                    "ConceptBuilder failed (non-fatal) | Source={Source}",
                    sourceCode);
            }
        }
    }
}
