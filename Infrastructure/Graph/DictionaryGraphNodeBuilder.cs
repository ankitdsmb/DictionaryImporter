using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Graph
{
    public sealed class DictionaryGraphNodeBuilder(
        string connectionString,
        ILogger<DictionaryGraphNodeBuilder> logger)
    {
        private readonly string _connectionString =
            connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private readonly ILogger<DictionaryGraphNodeBuilder> _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task BuildAsync(
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

            _logger.LogInformation(
                "GraphNodeBuilder started | Source={Source}",
                sourceCode);

            var sw = Stopwatch.StartNew();

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var result =
                    await conn.QuerySingleOrDefaultAsync<GraphNodeBuildResultRow>(
                        new CommandDefinition(
                            "sp_GraphNode_BuildAllBySource",
                            new { SourceCode = sourceCode },
                            commandType: CommandType.StoredProcedure,
                            cancellationToken: ct,
                            commandTimeout: 0));

                if (result is null)
                {
                    _logger.LogInformation(
                        "GraphNodeBuilder completed | Source={Source} | Inserted=0",
                        sourceCode);
                    return;
                }

                _logger.LogInformation(
                    "GraphNodeBuilder | Source={Source} | NodeType=Word | Inserted={Count}",
                    sourceCode,
                    result.WordInserted);

                _logger.LogInformation(
                    "GraphNodeBuilder | Source={Source} | NodeType=Sense | Inserted={Count}",
                    sourceCode,
                    result.SenseInserted);

                _logger.LogInformation(
                    "GraphNodeBuilder | Source={Source} | NodeType=Domain | Inserted={Count}",
                    sourceCode,
                    result.DomainInserted);

                _logger.LogInformation(
                    "GraphNodeBuilder | Source={Source} | NodeType=Language | Inserted={Count}",
                    sourceCode,
                    result.LanguageInserted);
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
                    "GraphNodeBuilder failed (non-fatal) | Source={Source}",
                    sourceCode);
            }
            finally
            {
                sw.Stop();

                _logger.LogInformation(
                    "GraphNodeBuilder completed | Source={Source} | DurationMs={Duration}",
                    sourceCode,
                    sw.ElapsedMilliseconds);
            }
        }

        private sealed class GraphNodeBuildResultRow
        {
            public long WordInserted { get; init; }
            public long SenseInserted { get; init; }
            public long DomainInserted { get; init; }
            public long LanguageInserted { get; init; }
        }
    }
}
