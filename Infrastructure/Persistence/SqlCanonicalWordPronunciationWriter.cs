using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlCanonicalWordPronunciationWriter(string connectionString)
    {
        public async Task WriteIfNotExistsAsync(
            long canonicalWordId,
            string localeCode,
            string ipa,
            CancellationToken ct)
        {
            if (canonicalWordId <= 0)
                return;

            if (string.IsNullOrWhiteSpace(localeCode))
                return;

            localeCode = Helper.NormalizeLocaleCode(localeCode);
            if (string.IsNullOrWhiteSpace(localeCode))
                return;

            ipa = Helper.NormalizeIpa(ipa);

            // If IPA becomes empty after normalization, skip (avoid junk rows)
            if (string.IsNullOrWhiteSpace(ipa))
                return;

            const string sql = """
                               IF NOT EXISTS (
                                   SELECT 1
                                   FROM dbo.CanonicalWordPronunciation WITH (NOLOCK)
                                   WHERE CanonicalWordId = @CanonicalWordId
                                     AND LocaleCode = @LocaleCode
                                     AND Ipa = @Ipa
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
                               """;

            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(ct);

                await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            CanonicalWordId = canonicalWordId,
                            LocaleCode = localeCode,
                            Ipa = ipa
                        },
                        cancellationToken: ct));
            }
            catch
            {
                // ✅ Never crash importer
            }
        }
    }
}
