using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryGraphRankCalculator(
        string connectionString,
        ILogger<DictionaryGraphRankCalculator> logger)
    {
        private readonly string _connectionString =
            connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private readonly ILogger<DictionaryGraphRankCalculator> _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task CalculateAsync(
            CancellationToken ct)
        {
            _logger.LogInformation("GraphRankCalculator started");

            var sw = Stopwatch.StartNew();

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var result =
                    await conn.QuerySingleOrDefaultAsync<GraphRankResultRow>(
                        new CommandDefinition(
                            "sp_GraphRank_RecalculateAll",
                            commandType: CommandType.StoredProcedure,
                            cancellationToken: ct,
                            commandTimeout: 0));

                if (result is null)
                {
                    _logger.LogInformation("GraphRankCalculator completed | NoResultReturned");
                    return;
                }

                _logger.LogInformation(
                    "GraphRankCalculator completed | ConceptRows={ConceptRows} | SenseRows={SenseRows} | WordRows={WordRows} | DurationMs={Duration}",
                    result.ConceptRows,
                    result.SenseRows,
                    result.WordRows,
                    sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // STRICT: never crash pipeline
                _logger.LogError(ex, "GraphRankCalculator failed (non-fatal)");
            }
            finally
            {
                sw.Stop();
            }
        }

        private sealed class GraphRankResultRow
        {
            public long ConceptRows { get; init; }
            public long SenseRows { get; init; }
            public long WordRows { get; init; }
        }
    }
}
