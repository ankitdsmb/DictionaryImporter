using IpaNormalizer = DictionaryImporter.Core.PreProcessing.IpaNormalizer;

namespace DictionaryImporter.Infrastructure.PostProcessing;

public sealed class DictionaryEntryLinguisticEnricher
{
    private readonly string _connectionString;
    private readonly ILogger<DictionaryEntryLinguisticEnricher> _logger;
    private readonly IPartOfSpeechInfererV2 _posInferer;

    public DictionaryEntryLinguisticEnricher(
        string connectionString,
        IPartOfSpeechInfererV2 posInferer,
        ILogger<DictionaryEntryLinguisticEnricher> logger)
    {
        _connectionString = connectionString;
        _posInferer = posInferer;
        _logger = logger;
    }

    // =====================================================
    // ENTRY POINT
    // =====================================================
    public async Task ExecuteAsync(
        string sourceCode,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Linguistic enrichment started | Source={SourceCode}",
            sourceCode);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await InferAndPersistPartOfSpeech(conn, sourceCode, ct);

        await BackfillExplicitPartOfSpeechConfidence(conn, sourceCode, ct);

        await PersistPartOfSpeechHistory(conn, sourceCode, ct);

        await ExtractSynonymsFromCrossReferences(conn, sourceCode, ct);

        // ✅ SINGLE SOURCE OF TRUTH FOR IPA
        await EnrichCanonicalWordIpaFromDefinition(conn, sourceCode, ct);

        _logger.LogInformation(
            "Linguistic enrichment completed | Source={SourceCode}",
            sourceCode);
    }

    // =====================================================
    // 1. PART OF SPEECH INFERENCE
    // =====================================================
    private async Task InferAndPersistPartOfSpeech(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "POS inference started | Source={SourceCode}",
            sourceCode);

        const string selectSql = """
                                 WITH RankedDefinitions AS
                                 (
                                     SELECT
                                         e.DictionaryEntryId,
                                         p.Definition,
                                         ROW_NUMBER() OVER
                                         (
                                             PARTITION BY e.DictionaryEntryId
                                             ORDER BY
                                                 CASE
                                                     WHEN p.ParentParsedId IS NULL THEN 0
                                                     WHEN p.SenseNumber IS NOT NULL THEN 1
                                                     ELSE 2
                                                 END
                                         ) AS rn
                                     FROM dbo.DictionaryEntry e
                                     JOIN dbo.DictionaryEntryParsed p
                                         ON p.DictionaryEntryId = e.DictionaryEntryId
                                     WHERE e.SourceCode = @SourceCode
                                       AND (e.PartOfSpeech IS NULL OR e.PartOfSpeech = 'unk')
                                 )
                                 SELECT
                                     DictionaryEntryId,
                                     Definition
                                 FROM RankedDefinitions
                                 WHERE rn = 1;
                                 """;

        var rows =
            await conn.QueryAsync<(long Id, string Definition)>(
                selectSql,
                new { SourceCode = sourceCode });

        var updated = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var result =
                _posInferer.InferWithConfidence(row.Definition);

            if (result.Pos == "unk")
                continue;

            const string updateSql = """
                                     UPDATE dbo.DictionaryEntry
                                     SET
                                         PartOfSpeech = @Pos,
                                         PartOfSpeechConfidence = @Confidence
                                     WHERE DictionaryEntryId = @Id
                                       AND (PartOfSpeech IS NULL OR PartOfSpeech = 'unk');
                                     """;

            var affected =
                await conn.ExecuteAsync(
                    updateSql,
                    new
                    {
                        row.Id,
                        result.Pos,
                        result.Confidence
                    });

            if (affected > 0)
                updated++;
        }

        _logger.LogInformation(
            "POS inference completed | Source={SourceCode} | Updated={Count}",
            sourceCode,
            updated);
    }

    // =====================================================
    // 1.5 EXPLICIT POS CONFIDENCE BACKFILL
    // =====================================================
    private async Task BackfillExplicitPartOfSpeechConfidence(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        const string sql = """
                           UPDATE dbo.DictionaryEntry
                           SET PartOfSpeechConfidence = 100
                           WHERE SourceCode = @SourceCode
                             AND PartOfSpeech IS NOT NULL
                             AND PartOfSpeechConfidence IS NULL;
                           """;

        var rows =
            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { SourceCode = sourceCode },
                    cancellationToken: ct));

        _logger.LogInformation(
            "POS confidence backfilled | Source={SourceCode} | Rows={Rows}",
            sourceCode,
            rows);
    }

    // =====================================================
    // 2. POS HISTORY / CONFIDENCE PERSISTENCE
    // =====================================================
    private async Task PersistPartOfSpeechHistory(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        const string sql = """
                           INSERT INTO dbo.DictionaryEntryPartOfSpeech
                           (
                               DictionaryEntryId,
                               PartOfSpeech,
                               Confidence,
                               Source,
                               CreatedUtc
                           )
                           SELECT
                               e.DictionaryEntryId,
                               LOWER(e.PartOfSpeech),
                               ISNULL(e.PartOfSpeechConfidence, 100),
                               e.SourceCode,
                               SYSUTCDATETIME()
                           FROM dbo.DictionaryEntry e
                           WHERE e.SourceCode = @SourceCode
                             AND e.PartOfSpeech IS NOT NULL
                             AND NOT EXISTS
                           (
                               SELECT 1
                               FROM dbo.DictionaryEntryPartOfSpeech p
                               WHERE p.DictionaryEntryId = e.DictionaryEntryId
                                 AND p.PartOfSpeech = LOWER(e.PartOfSpeech)
                           );
                           """;

        var rows =
            await conn.ExecuteAsync(sql, new { SourceCode = sourceCode });

        _logger.LogInformation(
            "POS history persisted | Source={SourceCode} | Rows={Rows}",
            sourceCode,
            rows);
    }

    // =====================================================
    // 3. SYNONYM EXTRACTION
    // =====================================================
    private async Task ExtractSynonymsFromCrossReferences(
        SqlConnection conn,
        string sourceCode,
        CancellationToken ct)
    {
        const string sql = """
                           INSERT INTO dbo.DictionaryEntrySynonym
                           (
                               DictionaryEntryParsedId,
                               SynonymText,
                               Source,
                               CreatedUtc
                           )
                           SELECT DISTINCT
                               cr.SourceParsedId,
                               LOWER(cr.TargetWord),
                               'crossref',
                               SYSUTCDATETIME()
                           FROM dbo.DictionaryEntryCrossReference cr
                           JOIN dbo.DictionaryEntryParsed p
                               ON p.DictionaryEntryParsedId = cr.SourceParsedId
                           JOIN dbo.DictionaryEntry e
                               ON e.DictionaryEntryId = p.DictionaryEntryId
                           WHERE e.SourceCode = @SourceCode
                             AND cr.ReferenceType IN ('See','SeeAlso')
                             AND NOT EXISTS
                           (
                               SELECT 1
                               FROM dbo.DictionaryEntrySynonym s
                               WHERE s.DictionaryEntryParsedId = cr.SourceParsedId
                                 AND s.SynonymText = LOWER(cr.TargetWord)
                           );
                           """;

        var rows =
            await conn.ExecuteAsync(sql, new { SourceCode = sourceCode });

        _logger.LogInformation(
            "Synonyms extracted | Source={SourceCode} | Rows={Rows}",
            sourceCode,
            rows);
    }

    // =====================================================
    // 4. IPA ENRICHMENT (SINGLE SOURCE OF TRUTH)
    // =====================================================
    private async Task EnrichCanonicalWordIpaFromDefinition(SqlConnection conn, string sourceCode, CancellationToken ct)
    {
        _logger.LogInformation(
            "IPA enrichment started | Source={SourceCode}",
            sourceCode);

        const string sql = """
                           SELECT DISTINCT
                               cw.CanonicalWordId,
                               p.RawFragment
                           FROM dbo.DictionaryEntryParsed p
                           JOIN dbo.DictionaryEntry e
                               ON e.DictionaryEntryId = p.DictionaryEntryId
                           JOIN dbo.CanonicalWord cw
                               ON cw.CanonicalWordId = e.CanonicalWordId
                           WHERE e.SourceCode = @SourceCode
                               AND p.RawFragment LIKE '%/%';
                           """;

        var rows =
            await conn.QueryAsync<(long CanonicalWordId, string RawFragment)>(
                sql,
                new { SourceCode = sourceCode });

        var inserted = 0;
        var candidates = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var ipaMap =
                GenericIpaExtractor.ExtractIpaWithLocale(row.RawFragment);

            if (ipaMap.Count == 0)
            {
                skipped++;
                continue;
            }

            candidates += ipaMap.Count;

            foreach (var kv in ipaMap)
            {
                var rawIpa = kv.Key;
                var locale = kv.Value;

                if (string.IsNullOrWhiteSpace(rawIpa) ||
                    string.IsNullOrWhiteSpace(locale))
                {
                    skipped++;
                    continue;
                }

                var normalizedIpa =
                    IpaNormalizer.Normalize(rawIpa);

                if (string.IsNullOrWhiteSpace(normalizedIpa))
                {
                    skipped++;
                    continue;
                }

                const string insertSql = """
                                         IF NOT EXISTS
                                         (
                                             SELECT 1
                                             FROM dbo.CanonicalWordPronunciation
                                             WHERE CanonicalWordId = @CanonicalWordId
                                                 AND LocaleCode = @LocaleCode
                                         )
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
                                         );
                                         """;

                var affected =
                    await conn.ExecuteAsync(
                        new CommandDefinition(
                            insertSql,
                            new
                            {
                                row.CanonicalWordId,
                                LocaleCode = locale,
                                Ipa = normalizedIpa
                            },
                            cancellationToken: ct));

                if (affected > 0)
                    inserted++;
            }
        }

        _logger.LogInformation(
            "IPA enrichment completed | Source={SourceCode} | Candidates={Candidates} | Inserted={Inserted} | Skipped={Skipped}",
            sourceCode,
            candidates,
            inserted,
            skipped);
    }
}