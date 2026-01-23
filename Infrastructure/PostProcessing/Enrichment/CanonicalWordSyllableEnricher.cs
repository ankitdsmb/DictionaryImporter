using DictionaryImporter.Common;

namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment
{
    public sealed class CanonicalWordSyllableEnricher(
        string connectionString,
        ILogger<CanonicalWordSyllableEnricher> logger)
    {
        public async Task ExecuteAsync(
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(connectionString);
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

                var syllables = Helper.IpaSyllabifier.Split(row.Ipa);
                var cleaned = Helper.IpaSyllablePostProcessor.Normalize(syllables);

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

            logger.LogInformation("IPA syllable enrichment completed");
        }
    }
}