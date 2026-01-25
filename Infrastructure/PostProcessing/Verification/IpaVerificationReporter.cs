using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.PostProcessing.Verification;

public sealed class IpaVerificationReporter(
    string connectionString,
    ILogger<IpaVerificationReporter> logger)
{
    private readonly string _connectionString =
        connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private readonly ILogger<IpaVerificationReporter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ReportAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var rows =
                await conn.QueryAsync<IpaLocaleCountRow>(
                    new CommandDefinition(
                        "sp_CanonicalWordPronunciation_VerifyByLocale",
                        commandType: System.Data.CommandType.StoredProcedure,
                        cancellationToken: ct));

            var list = rows?.ToList();

            if (list == null || list.Count == 0)
            {
                _logger.LogInformation("IPA verification: no pronunciation data loaded");
                return;
            }

            foreach (var row in list)
            {
                _logger.LogInformation(
                    "IPA verification | Locale={Locale} | Words={Count}",
                    row.LocaleCode,
                    row.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IPA verification failed (non-fatal)");
        }
    }

    private sealed class IpaLocaleCountRow
    {
        public string LocaleCode { get; init; } = string.Empty;
        public int Count { get; init; }
    }
}