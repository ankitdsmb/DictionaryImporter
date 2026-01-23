using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
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

            localeCode = NormalizeLocaleCode(localeCode);
            if (string.IsNullOrWhiteSpace(localeCode))
                return;

            ipa = NormalizeIpa(ipa);

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

        // NEW METHOD (added)
        private static string NormalizeLocaleCode(string localeCode)
        {
            if (string.IsNullOrWhiteSpace(localeCode))
                return string.Empty;

            var t = localeCode.Trim();

            // keep it simple and stable
            t = t.Replace('_', '-');

            if (t.Length > 15)
                t = t.Substring(0, 15);

            return t;
        }

        // NEW METHOD (added)
        private static string NormalizeIpa(string? ipa)
        {
            if (string.IsNullOrWhiteSpace(ipa))
                return string.Empty;

            var t = ipa.Trim();

            // remove wiki/template remnants if any
            t = t.Replace("[[", "").Replace("]]", "");
            t = t.Replace("{{", "").Replace("}}", "");

            // collapse whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            // hard safety cap
            if (t.Length > 300)
                t = t.Substring(0, 300).Trim();

            // must contain at least something meaningful (IPA symbols are not only A-Z)
            if (t.Length < 2)
                return string.Empty;

            return t;
        }
    }
}
