namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment;

public sealed class CanonicalWordSyllableEnricher
{
    private readonly string _connectionString;
    private readonly ILogger<CanonicalWordSyllableEnricher> _logger;

    public CanonicalWordSyllableEnricher(
        string connectionString,
        ILogger<CanonicalWordSyllableEnricher> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var ipas =
            await conn.QueryAsync<(long CanonicalWordId, string LocaleCode, string Ipa)>(
                """
                SELECT CanonicalWordId, LocaleCode, Ipa
                FROM dbo.CanonicalWordPronunciation
                """);

        foreach (var row in ipas)
        {
            ct.ThrowIfCancellationRequested();

            // 1. Split into syllables
            var syllables = IpaSyllabifier.Split(row.Ipa);
            var cleaned = IpaSyllablePostProcessor.Normalize(syllables);

            // 3. Persist syllables
            foreach (var s in cleaned)
                await conn.ExecuteAsync(
                    """
                    IF NOT EXISTS
                    (
                        SELECT 1
                        FROM dbo.CanonicalWordSyllable
                        WHERE CanonicalWordId = @Id
                          AND LocaleCode = @Locale
                          AND SyllableIndex = @Index
                    )
                    INSERT INTO dbo.CanonicalWordSyllable
                    (
                        CanonicalWordId,
                        LocaleCode,
                        SyllableIndex,
                        SyllableText,
                        StressLevel,
                        CreatedUtc
                    )
                    VALUES
                    (
                        @Id,
                        @Locale,
                        @Index,
                        @Text,
                        @Stress,
                        SYSUTCDATETIME()
                    );
                    """,
                    new
                    {
                        Id = row.CanonicalWordId,
                        Locale = row.LocaleCode,
                        s.Index,
                        s.Text,
                        Stress = s.StressLevel
                    });
        }

        _logger.LogInformation("IPA syllable enrichment completed");
    }
}