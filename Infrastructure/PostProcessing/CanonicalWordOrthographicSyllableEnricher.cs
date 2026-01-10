using Dapper;
using DictionaryImporter.Core.Linguistics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment
{
    public sealed class CanonicalWordOrthographicSyllableEnricher
    {
        private readonly string _connectionString;
        private readonly ILogger<CanonicalWordOrthographicSyllableEnricher> _logger;

        public CanonicalWordOrthographicSyllableEnricher(
            string connectionString,
            ILogger<CanonicalWordOrthographicSyllableEnricher> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task ExecuteAsync(
            string localeCode,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Orthographic syllable enrichment started | Locale={Locale}",
                localeCode);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var words =
                await conn.QueryAsync<(long Id, string Word)>(
                    """
                    SELECT
                        CanonicalWordId,
                        NormalizedWord
                    FROM dbo.CanonicalWord
                    WHERE NormalizedWord IS NOT NULL;
                    """);

            int inserted = 0;
            int skipped = 0;

            foreach (var w in words)
            {
                ct.ThrowIfCancellationRequested();

                var syllables =
                    OrthographicSyllableExtractor.Extract(w.Word);

                if (syllables.Count == 0)
                {
                    skipped++;
                    continue;
                }

                for (int i = 0; i < syllables.Count; i++)
                {
                    var affected =
                        await conn.ExecuteAsync(
                            """
                            IF NOT EXISTS
                            (
                                SELECT 1
                                FROM dbo.CanonicalWordOrthographicSyllable
                                WHERE CanonicalWordId = @Id
                                  AND LocaleCode = @Locale
                                  AND SyllableIndex = @Index
                            )
                            INSERT INTO dbo.CanonicalWordOrthographicSyllable
                            (
                                CanonicalWordId,
                                LocaleCode,
                                SyllableIndex,
                                SyllableText
                            )
                            VALUES
                            (
                                @Id,
                                @Locale,
                                @Index,
                                @Text
                            );
                            """,
                            new
                            {
                                Id = w.Id,
                                Locale = localeCode,
                                Index = i + 1,
                                Text = syllables[i]
                            });

                    if (affected > 0)
                        inserted++;
                }
            }

            _logger.LogInformation(
                "Orthographic syllable enrichment completed | Locale={Locale} | Inserted={Inserted} | Skipped={Skipped}",
                localeCode,
                inserted,
                skipped);
        }
    }
}
