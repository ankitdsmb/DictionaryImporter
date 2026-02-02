using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class DictionaryImportControl(
    string connectionString,
    ILogger<DictionaryImportControl> logger)
    : IDictionaryImportControl
{
    public async Task<bool> MarkSourceCompletedAsync(
        string sourceCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
            throw new ArgumentNullException(nameof(sourceCode));

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var p = new DynamicParameters();
        p.Add("@SourceCode", sourceCode, DbType.String, ParameterDirection.Input);
        p.Add("@AllCompleted", dbType: DbType.Boolean, direction: ParameterDirection.Output, size: 1);

        await conn.ExecuteAsync(
                "dbo.sp_DictionaryImport_SourceCompleted",
                p,
                commandType: CommandType.StoredProcedure)
            .ConfigureAwait(false);

        return p.Get<bool>("@AllCompleted");
    }

    private const int MaxFinalizeRetries = 20;

    public async Task TryFinalizeAsync(string sourceCode, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxFinalizeRetries; attempt++)
        {
            try
            {
                await ExecuteFinalizeOnceAsync(sourceCode, ct);
                return;
            }
            catch (SqlException ex) when (ex.Number == 56002)
            {
                // Global finalize lock busy — EXPECTED
                logger.LogInformation(
                    "Finalize already running for source | Attempt={Attempt}/{Max} | Source={Source}",
                    attempt, MaxFinalizeRetries, sourceCode);

                await Task.Delay(1000, ct);
            }
            catch (SqlException ex) when (ex.Number == 56020)
            {
                // Global finalize lock busy — EXPECTED
                logger.LogInformation(
                    "Finalize deferred (global lock busy) | Attempt={Attempt}/{Max} | Source={Source}",
                    attempt, MaxFinalizeRetries, sourceCode);

                await Task.Delay(1000, ct);
            }
            catch (SqlException ex) when (ex.Number == 1205)
            {
                // Deadlock retry (still possible in rare cases)
                logger.LogWarning(
                    "Deadlock during finalize, retrying | Attempt={Attempt}/{Max} | Source={Source}",
                    attempt, MaxFinalizeRetries, sourceCode);

                await Task.Delay(500 * attempt, ct);
            }
        }

        throw new InvalidOperationException(
            $"Finalize failed after {MaxFinalizeRetries} retries | Source={sourceCode}");
    }

    private async Task ExecuteFinalizeOnceAsync(string sourceCode, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var p = new DynamicParameters();
        p.Add("@Finalize", true, DbType.Boolean);
        p.Add("@SourceCode", sourceCode, DbType.String);

        await conn.ExecuteAsync(
            "dbo.sp_DictionaryEntryStaging_InsertFast",
            p,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 600);
    }
}