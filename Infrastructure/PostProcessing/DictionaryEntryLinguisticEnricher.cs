using IpaNormalizer = DictionaryImporter.Core.PreProcessing.IpaNormalizer;

namespace DictionaryImporter.Infrastructure.PostProcessing
{
    public sealed class DictionaryEntryLinguisticEnricher(
        string connectionString,
        IPartOfSpeechInfererV2 posInferer,
        IDictionaryEntryPartOfSpeechRepository posRepository,
        ILogger<DictionaryEntryLinguisticEnricher> logger)
    {
        private readonly string _connectionString =
            connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private readonly IPartOfSpeechInfererV2 _posInferer =
            posInferer ?? throw new ArgumentNullException(nameof(posInferer));

        private readonly IDictionaryEntryPartOfSpeechRepository _posRepository =
            posRepository ?? throw new ArgumentNullException(nameof(posRepository));

        private readonly ILogger<DictionaryEntryLinguisticEnricher> _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode;

            _logger.LogInformation(
                "Linguistic enrichment started | SourceCode={SourceCode}",
                sourceCode);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await InferAndPersistPartOfSpeech(sourceCode, ct);

            await BackfillExplicitPartOfSpeechConfidence(sourceCode, ct);

            await PersistPartOfSpeechHistory(sourceCode, ct);

            await ExtractSynonymsFromCrossReferences(conn, sourceCode, ct);

            await EnrichCanonicalWordIpaFromDefinition(conn, sourceCode, ct);

            _logger.LogInformation(
                "Linguistic enrichment completed | SourceCode={SourceCode}",
                sourceCode);
        }

        // ============================================================
        // Part of Speech (Repository-driven)
        // ============================================================

        private async Task InferAndPersistPartOfSpeech(
            string sourceCode,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "POS inference started | SourceCode={SourceCode}",
                sourceCode);

            var rows =
                await _posRepository.GetEntriesNeedingPosAsync(sourceCode, ct);

            var updated = 0;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(row.Definition))
                    continue;

                var result =
                    _posInferer.InferWithConfidence(row.Definition);

                if (string.IsNullOrWhiteSpace(result.Pos) || result.Pos == "unk")
                    continue;

                var affected =
                    await _posRepository.UpdatePartOfSpeechIfUnknownAsync(
                        row.EntryId,
                        result.Pos,
                        result.Confidence,
                        ct);

                if (affected > 0)
                    updated++;
            }

            _logger.LogInformation(
                "POS inference completed | SourceCode={SourceCode} | Updated={Count}",
                sourceCode,
                updated);
        }

        private async Task BackfillExplicitPartOfSpeechConfidence(
            string sourceCode,
            CancellationToken ct)
        {
            var rows =
                await _posRepository.BackfillConfidenceAsync(sourceCode, ct);

            _logger.LogInformation(
                "POS confidence backfilled | SourceCode={SourceCode} | Rows={Rows}",
                sourceCode,
                rows);
        }

        private async Task PersistPartOfSpeechHistory(
            string sourceCode,
            CancellationToken ct)
        {
            await _posRepository.PersistHistoryAsync(sourceCode, ct);

            _logger.LogInformation(
                "POS history persisted | SourceCode={SourceCode}",
                sourceCode);
        }

        // ============================================================
        // Synonyms extracted from CrossRefs (FIXED: SourceCode)
        // ============================================================

        private async Task ExtractSynonymsFromCrossReferences(
            SqlConnection conn,
            string sourceCode,
            CancellationToken ct)
        {
            // ✅ FIX:
            // Table dbo.DictionaryEntrySynonym has SourceCode column (NOT Source).
            // Also keep SourceCode for extracted synonyms = "CROSSREF"
            const string sql = """
                               INSERT INTO dbo.DictionaryEntrySynonym
                               (
                                   DictionaryEntryParsedId,
                                   SynonymText,
                                   SourceCode,
                                   CreatedUtc,
                                   HasNonEnglishText,
                                   NonEnglishTextId
                               )
                               SELECT DISTINCT
                                   cr.SourceParsedId,
                                   LOWER(cr.TargetWord),
                                   'CROSSREF',
                                   SYSUTCDATETIME(),
                                   0,
                                   NULL
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
                                     AND s.SourceCode = 'CROSSREF'
                               );
                               """;

            var rows =
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new { SourceCode = sourceCode },
                        cancellationToken: ct));

            _logger.LogInformation(
                "Synonyms extracted from cross-references | SourceCode={SourceCode} | Rows={Rows}",
                sourceCode,
                rows);
        }

        // ============================================================
        // IPA enrichment (no change, only safety)
        // ============================================================

        private async Task EnrichCanonicalWordIpaFromDefinition(
            SqlConnection conn,
            string sourceCode,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "IPA enrichment started | SourceCode={SourceCode}",
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
                    new CommandDefinition(
                        sql,
                        new { SourceCode = sourceCode },
                        cancellationToken: ct));

            var inserted = 0;
            var candidates = 0;
            var skipped = 0;

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(row.RawFragment))
                {
                    skipped++;
                    continue;
                }

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
                                                 );
                                             END
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
                "IPA enrichment completed | SourceCode={SourceCode} | Candidates={Candidates} | Inserted={Inserted} | Skipped={Skipped}",
                sourceCode,
                candidates,
                inserted,
                skipped);
        }
    }
}
