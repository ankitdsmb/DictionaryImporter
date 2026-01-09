using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlCanonicalWordPronunciationWriter
    {
        private readonly string _connectionString;

        public SqlCanonicalWordPronunciationWriter(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task WriteAsync(
            long canonicalWordId,
            string localeCode,
            string ipa,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);

            await conn.ExecuteAsync(
                """
                IF NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.CanonicalWordPronunciation
                    WHERE CanonicalWordId = @CanonicalWordId
                      AND LocaleCode = @LocaleCode
                )
                BEGIN
                    INSERT INTO dbo.CanonicalWordPronunciation
                    (
                        CanonicalWordId,
                        LocaleCode,
                        Ipa
                    )
                    VALUES
                    (
                        @CanonicalWordId,
                        @LocaleCode,
                        @Ipa
                    )
                END
                """,
                new
                {
                    CanonicalWordId = canonicalWordId,
                    LocaleCode = localeCode,
                    Ipa = ipa
                });
        }
    }
}
