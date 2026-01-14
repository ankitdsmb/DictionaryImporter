namespace DictionaryImporter.Infrastructure.PostProcessing.Verification;

public sealed class IpaVerificationReporter(
    string connectionString,
    ILogger<IpaVerificationReporter> logger)
{
    public async Task ReportAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
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
            logger.LogInformation(
                "IPA verification: no pronunciation data loaded");
            return;
        }

        foreach (var row in rows)
            logger.LogInformation(
                "IPA verification | Locale={Locale} | Words={Count}",
                row.Locale,
                row.Count);
    }
}