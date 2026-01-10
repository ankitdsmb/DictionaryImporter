using Dapper;
using DictionaryImporter.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment
{
    public sealed class CanonicalWordIpaEnricher
    {
        private readonly string _connectionString;
        private readonly SqlCanonicalWordPronunciationWriter _writer;
        private readonly ILogger<CanonicalWordIpaEnricher> _logger;

        public CanonicalWordIpaEnricher(
            string connectionString,
            SqlCanonicalWordPronunciationWriter writer,
            ILogger<CanonicalWordIpaEnricher> logger)
        {
            _connectionString = connectionString;
            _writer = writer;
            _logger = logger;
        }

        public async Task ExecuteAsync(
            string localeCode,
            string ipaFilePath,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Canonical IPA enrichment started | Locale={Locale} | File={File}",
                localeCode,
                ipaFilePath);

            // 1. Load IPA file
            var ipaEntries =
                IpaFileLoader.Load(ipaFilePath)
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Word) &&
                        !string.IsNullOrWhiteSpace(x.Ipa))
                    .Select(x => new
                    {
                        Word = Normalize(x.Word),
                        Ipa = x.Ipa.Trim()
                    })
                    .ToList();

            _logger.LogInformation(
                "IPA file loaded | Entries={Count}",
                ipaEntries.Count);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // 2. Load canonical words
            var canonicalWords =
                (await conn.QueryAsync<(long Id, string NormalizedWord)>(
                    """
                    SELECT CanonicalWordId, NormalizedWord
                    FROM dbo.CanonicalWord
                    """))
                .ToDictionary(
                    x => x.NormalizedWord,
                    x => x.Id,
                    StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Canonical words loaded | Count={Count}",
                canonicalWords.Count);

            int inserted = 0;
            int skipped = 0;
            int matchedWords = 0;

            // 3. Group IPA by normalized word
            foreach (var group in ipaEntries.GroupBy(x => x.Word))
            {
                ct.ThrowIfCancellationRequested();

                if (!canonicalWords.TryGetValue(group.Key, out var canonicalWordId))
                {
                    skipped += group.Count();

                    _logger.LogDebug(
                        "No canonical match for IPA word | Word={Word} | Count={Count}",
                        group.Key,
                        group.Count());

                    continue;
                }

                matchedWords++;

                foreach (var entry in group)
                {
                    ct.ThrowIfCancellationRequested();

                    await _writer.WriteIfNotExistsAsync(
                        canonicalWordId,
                        localeCode,
                        entry.Ipa,
                        ct);

                    inserted++;
                }
            }

            _logger.LogInformation(
                "Canonical IPA enrichment completed | Locale={Locale} | File={File} | Loaded={Loaded} | MatchedWords={Matched} | Inserted={Inserted} | Skipped={Skipped}",
                localeCode,
                ipaFilePath,
                ipaEntries.Count,
                matchedWords,
                inserted,
                skipped);
        }

        // Must match CanonicalWord normalization rules
        private static string Normalize(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            var text = word.ToLowerInvariant();
            text = Regex.Replace(text, @"[^\p{L}\s]", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }
    }
}
