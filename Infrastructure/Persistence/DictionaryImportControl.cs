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
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var p = new DynamicParameters();
        p.Add("@SourceCode", sourceCode, DbType.String, ParameterDirection.Input);
        p.Add("@AllCompleted", dbType: DbType.Boolean, direction: ParameterDirection.Output);

        await conn.ExecuteAsync(
            "dbo.sp_DictionaryImport_SourceCompleted",
            p,
            commandType: CommandType.StoredProcedure);

        return p.Get<bool>("@AllCompleted");
    }

    public async Task TryFinalizeAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var p = new DynamicParameters();
        p.Add("@Finalize", 1);

        try
        {
            await conn.ExecuteAsync(
                "dbo.sp_DictionaryEntryStaging_InsertFast",
                p,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 600)
                .ConfigureAwait(false);

            logger.LogInformation("Dictionary staging finalized successfully.");
        }
        catch (SqlException ex) when (ex.Number == 56001)
        {
            logger.LogDebug(
                "Finalize skipped: not all sources completed yet.");
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            logger.LogDebug(
                "Finalize skipped: another finalize already in progress.");
        }
    }
}