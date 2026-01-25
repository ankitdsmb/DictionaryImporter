using DictionaryImporter.Common;

namespace DictionaryImporter.Infrastructure.Merge;

public sealed class SqlDictionaryEntryMergeExecutor(
    string connectionString,
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlDictionaryEntryMergeExecutor> logger)
    : IDataMergeExecutor
{
    private readonly string _connectionString =
        connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly ISqlStoredProcedureExecutor _sp =
        sp ?? throw new ArgumentNullException(nameof(sp));

    private readonly ILogger<SqlDictionaryEntryMergeExecutor> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ExecuteAsync(
        string sourceCode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        _logger.LogInformation(
            "Merge started | SourceCode={SourceCode}",
            sourceCode);

        try
        {
            var statsList =
                await _sp.QueryAsync<StagingStatsRow>(
                    "sp_DictionaryEntryStaging_GetStatsBySource",
                    new { SourceCode = sourceCode },
                    ct);

            var stats =
                statsList?.FirstOrDefault()
                ?? new StagingStatsRow { TotalRows = 0, UniqueKeys = 0 };

            var duplicateCount = stats.TotalRows - stats.UniqueKeys;

            _logger.LogInformation(
                "Staging analysis | Source={SourceCode} | Total={Total} | Unique={Unique} | Duplicates={Duplicates}",
                sourceCode,
                stats.TotalRows,
                stats.UniqueKeys,
                duplicateCount);

            var inserted =
                await _sp.ExecuteScalarAsync<long>(
                    "sp_DictionaryEntry_MergeFromStaging_BySource",
                    new { SourceCode = sourceCode },
                    ct,
                    timeoutSeconds: 0);

            _logger.LogInformation(
                "Merge completed | SourceCode={SourceCode} | Inserted={Inserted}",
                sourceCode,
                inserted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // STRICT: Never crash pipeline
            _logger.LogError(
                ex,
                "Merge failed (non-fatal) | SourceCode={SourceCode}. Staging preserved.",
                sourceCode);
        }
    }

    private sealed class StagingStatsRow
    {
        public long TotalRows { get; init; }
        public long UniqueKeys { get; init; }
    }
}