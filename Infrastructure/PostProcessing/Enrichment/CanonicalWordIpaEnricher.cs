namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment;

public sealed class CanonicalWordIpaEnricher(
    string connectionString,
    SqlCanonicalWordPronunciationWriter writer,
    ILogger<CanonicalWordIpaEnricher> logger)
{
    public async Task ExecuteAsync(
        string localeCode,
        string ipaFilePath,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Canonical IPA enrichment started | Locale={Locale} | File={File}",
            localeCode,
            ipaFilePath);

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

        logger.LogInformation(
            "IPA file loaded | Entries={Count}",
            ipaEntries.Count);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

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

        logger.LogInformation(
            "Canonical words loaded | Count={Count}",
            canonicalWords.Count);

        var inserted = 0;
        var skipped = 0;
        var matchedWords = 0;

        foreach (var group in ipaEntries.GroupBy(x => x.Word))
        {
            ct.ThrowIfCancellationRequested();

            if (!canonicalWords.TryGetValue(group.Key, out var canonicalWordId))
            {
                skipped += group.Count();

                logger.LogDebug(
                    "No canonical match for IPA word | Word={Word} | Count={Count}",
                    group.Key,
                    group.Count());

                continue;
            }

            matchedWords++;

            foreach (var entry in group)
            {
                ct.ThrowIfCancellationRequested();

                await writer.WriteIfNotExistsAsync(
                    canonicalWordId,
                    localeCode,
                    entry.Ipa,
                    ct);

                inserted++;
            }
        }

        logger.LogInformation(
            "Canonical IPA enrichment completed | Locale={Locale} | File={File} | Loaded={Loaded} | MatchedWords={Matched} | Inserted={Inserted} | Skipped={Skipped}",
            localeCode,
            ipaFilePath,
            ipaEntries.Count,
            matchedWords,
            inserted,
            skipped);
    }

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