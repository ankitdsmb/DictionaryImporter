namespace DictionaryImporter.Gateway.Grammar.Infrastructure;

public sealed class GrammarStartupCleanup(
    string connectionString,
    ILogger<GrammarStartupCleanup> logger)
{
    private readonly string _connectionString = connectionString
                                                ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly ILogger<GrammarStartupCleanup> _logger = logger
                                                              ?? throw new ArgumentNullException(nameof(logger));

    private const int CommandTimeoutSeconds = 60;
    private const int StaleAfterMinutes = 30;
    private const int MaxRetries = 3;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                _logger.LogInformation(
                    "Grammar startup cleanup started (Attempt {Attempt})",
                    attempt);

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var affected = await conn.ExecuteAsync(
                    "dbo.sp_Grammar_ClearStaleClaims",
                    new { StaleAfterMinutes },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: CommandTimeoutSeconds);

                _logger.LogInformation(
                    "Grammar startup cleanup completed successfully. RowsAffected={Rows}",
                    affected);

                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Grammar startup cleanup cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Grammar startup cleanup failed (Attempt {Attempt})",
                    attempt);

                if (attempt >= MaxRetries)
                {
                    _logger.LogError(
                        "Grammar startup cleanup aborted after {Retries} attempts",
                        MaxRetries);
                    return; // DO NOT crash app
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}