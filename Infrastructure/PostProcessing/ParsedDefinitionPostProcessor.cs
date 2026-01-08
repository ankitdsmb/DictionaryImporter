using Dapper;
using DictionaryImporter.Core.Linguistics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.PostProcessing
{
    public sealed class DictionaryEntryLinguisticEnricher
    {
        private readonly string _connectionString;
        private readonly IPartOfSpeechInfererV2 _posInferer;
        private readonly ILogger<DictionaryEntryLinguisticEnricher> _logger;

        public DictionaryEntryLinguisticEnricher(
            string connectionString,
            IPartOfSpeechInfererV2 posInferer,
            ILogger<DictionaryEntryLinguisticEnricher> logger)
        {
            _connectionString = connectionString;
            _posInferer = posInferer;
            _logger = logger;
        }

        public async Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await InferAndPersistPartOfSpeech(conn, sourceCode, ct);
            await PersistPartOfSpeechHistory(conn, sourceCode, ct);
            await ExtractSynonymsFromCrossReferences(conn, sourceCode, ct);

            _logger.LogInformation(
                "Linguistic enrichment completed | Source={SourceCode}",
                sourceCode);
        }

        // =====================================================
        // 1. PART OF SPEECH INFERENCE (WRITE-ONCE)
        // =====================================================
        private async Task InferAndPersistPartOfSpeech(
            SqlConnection conn,
            string sourceCode,
            CancellationToken ct)
        {
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

            int updated = 0;

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
                            Id = row.Id,
                            Pos = result.Pos,
                            Confidence = result.Confidence
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
                e.PartOfSpeechConfidence,
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
        // 3. SYNONYM EXTRACTION (FROM CROSS-REFERENCES)
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
    }
}