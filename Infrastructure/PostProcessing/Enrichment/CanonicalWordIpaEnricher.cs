using Dapper;
using Microsoft.Data.SqlClient;
using DictionaryImporter.Infrastructure.Persistence;

namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment
{
    public sealed class CanonicalWordIpaEnricher
    {
        private readonly string _connectionString;
        private readonly SqlCanonicalWordPronunciationWriter _writer;

        public CanonicalWordIpaEnricher(
            string connectionString,
            SqlCanonicalWordPronunciationWriter writer)
        {
            _connectionString = connectionString;
            _writer = writer;
        }

        public async Task ExecuteAsync(
            string localeCode,
            string ipaFilePath,
            CancellationToken ct)
        {
            var ipaMap =
                IpaFileLoader.Load(ipaFilePath)
                    .ToDictionary(x => x.Word, x => x.Ipa);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var words =
                await conn.QueryAsync<(long Id, string Word)>(
                    """
                    SELECT CanonicalWordId, NormalizedWord
                    FROM dbo.CanonicalWord
                    """);

            foreach (var w in words)
            {
                ct.ThrowIfCancellationRequested();

                if (!ipaMap.TryGetValue(w.Word, out var ipa))
                    continue;

                await _writer.WriteAsync(
                    w.Id,
                    localeCode,
                    ipa,
                    ct);
            }
        }
    }
}