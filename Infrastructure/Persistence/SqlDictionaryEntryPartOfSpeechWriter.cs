using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryPartOfSpeechRepository(
        string connectionString,
        ILogger<SqlDictionaryEntryPartOfSpeechRepository> logger)
        : IDictionaryEntryPartOfSpeechRepository
    {
        private readonly string _connectionString =
            connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        private readonly ILogger<SqlDictionaryEntryPartOfSpeechRepository> _logger =
            logger ?? throw new ArgumentNullException(nameof(logger));

        public async Task PersistHistoryAsync(string sourceCode, CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            const string sql = """
                INSERT INTO dbo.DictionaryEntryPartOfSpeech
                (
                    DictionaryEntryId,
                    PartOfSpeech,
                    Confidence,
                    SourceCode,
                    CreatedUtc
                )
                SELECT
                    e.DictionaryEntryId,
                    LOWER(LTRIM(RTRIM(e.PartOfSpeech))),
                    ISNULL(e.PartOfSpeechConfidence, 100),
                    e.SourceCode,
                    SYSUTCDATETIME()
                FROM dbo.DictionaryEntry e
                WHERE e.SourceCode = @SourceCode
                  AND e.PartOfSpeech IS NOT NULL
                  AND LTRIM(RTRIM(e.PartOfSpeech)) <> ''
                  AND LOWER(LTRIM(RTRIM(e.PartOfSpeech))) <> 'unk'
                  AND NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DictionaryEntryPartOfSpeech p
                    WHERE p.DictionaryEntryId = e.DictionaryEntryId
                      AND p.PartOfSpeech = LOWER(LTRIM(RTRIM(e.PartOfSpeech)))
                      AND p.SourceCode = e.SourceCode
                );
                """;

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows =
                    await conn.ExecuteAsync(
                        new CommandDefinition(
                            sql,
                            new { SourceCode = sourceCode },
                            cancellationToken: ct));

                _logger.LogInformation(
                    "POS history persisted | SourceCode={SourceCode} | Rows={Rows}",
                    sourceCode,
                    rows);
            }
            catch (Exception ex)
            {
                // ✅ Never crash import
                _logger.LogDebug(
                    ex,
                    "Failed to persist POS history | SourceCode={SourceCode}",
                    sourceCode);
            }
        }

        public async Task<IReadOnlyList<(long EntryId, string Definition)>> GetEntriesNeedingPosAsync(
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            const string sql = """
                WITH RankedDefinitions AS
                (
                    SELECT
                        e.DictionaryEntryId AS EntryId,
                        ISNULL(p.Definition, '') AS Definition,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY e.DictionaryEntryId
                            ORDER BY
                                CASE
                                    WHEN p.ParentParsedId IS NULL THEN 0
                                    WHEN p.SenseNumber IS NOT NULL THEN 1
                                    ELSE 2
                                END,
                                p.DictionaryEntryParsedId ASC
                        ) AS rn
                    FROM dbo.DictionaryEntry e
                    JOIN dbo.DictionaryEntryParsed p
                        ON p.DictionaryEntryId = e.DictionaryEntryId
                    WHERE e.SourceCode = @SourceCode
                      AND (e.PartOfSpeech IS NULL OR e.PartOfSpeech = 'unk')
                      AND p.Definition IS NOT NULL
                      AND LTRIM(RTRIM(p.Definition)) <> ''
                )
                SELECT
                    EntryId,
                    Definition
                FROM RankedDefinitions
                WHERE rn = 1;
                """;

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows =
                    await conn.QueryAsync<(long EntryId, string Definition)>(
                        new CommandDefinition(
                            sql,
                            new { SourceCode = sourceCode },
                            cancellationToken: ct));

                return rows.ToList();
            }
            catch (Exception ex)
            {
                // ✅ Never crash import
                _logger.LogDebug(
                    ex,
                    "Failed to fetch entries needing POS | SourceCode={SourceCode}",
                    sourceCode);

                return Array.Empty<(long EntryId, string Definition)>();
            }
        }

        public async Task<int> UpdatePartOfSpeechIfUnknownAsync(
            long entryId,
            string pos,
            int confidence,
            CancellationToken ct)
        {
            pos = string.IsNullOrWhiteSpace(pos) ? "unk" : pos.Trim().ToLowerInvariant();
            confidence = Math.Clamp(confidence, 0, 100);

            if (entryId <= 0)
                return 0;

            if (pos == "unk")
                return 0;

            const string sql = """
                UPDATE dbo.DictionaryEntry
                SET
                    PartOfSpeech = @Pos,
                    PartOfSpeechConfidence = @Confidence
                WHERE DictionaryEntryId = @EntryId
                  AND (PartOfSpeech IS NULL OR PartOfSpeech = 'unk');
                """;

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                return await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            EntryId = entryId,
                            Pos = pos,
                            Confidence = confidence
                        },
                        cancellationToken: ct));
            }
            catch (Exception ex)
            {
                // ✅ Never crash import
                _logger.LogDebug(
                    ex,
                    "Failed to update POS | EntryId={EntryId} | Pos={Pos} | Confidence={Confidence}",
                    entryId, pos, confidence);

                return 0;
            }
        }

        public async Task<int> BackfillConfidenceAsync(
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            const string sql = """
                UPDATE dbo.DictionaryEntry
                SET PartOfSpeechConfidence = 100
                WHERE SourceCode = @SourceCode
                  AND PartOfSpeech IS NOT NULL
                  AND PartOfSpeechConfidence IS NULL;
                """;

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                return await conn.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        new { SourceCode = sourceCode },
                        cancellationToken: ct));
            }
            catch (Exception ex)
            {
                // ✅ Never crash import
                _logger.LogDebug(
                    ex,
                    "Failed to backfill POS confidence | SourceCode={SourceCode}",
                    sourceCode);

                return 0;
            }
        }
    }
}
