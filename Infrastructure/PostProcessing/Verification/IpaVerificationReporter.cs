using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.PostProcessing.Verification
{
    public sealed class IpaVerificationReporter
    {
        private readonly string _connectionString;
        private readonly ILogger<IpaVerificationReporter> _logger;

        public IpaVerificationReporter(
            string connectionString,
            ILogger<IpaVerificationReporter> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task ReportAsync(CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var rows =
                await conn.QueryAsync<(string Locale, int Count)>(
                    """
                    SELECT LocaleCode, COUNT(*) AS Count
                    FROM dbo.CanonicalWordPronunciation
                    GROUP BY LocaleCode
                    """);

            if (!rows.Any())
            {
                _logger.LogInformation(
                    "IPA verification: no pronunciation data loaded");
                return;
            }

            foreach (var row in rows)
            {
                _logger.LogInformation(
                    "IPA verification | Locale={Locale} | Words={Count}",
                    row.Locale,
                    row.Count);
            }
        }
    }
}   