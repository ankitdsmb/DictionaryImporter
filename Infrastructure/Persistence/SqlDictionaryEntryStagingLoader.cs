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
    public sealed class SqlDictionaryEntryStagingLoader(
        string connectionString,
        ILogger<SqlDictionaryEntryStagingLoader> logger)
        : IStagingLoader
    {
        private static readonly DateTime SqlMinDate = new(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public async Task LoadAsync(
            IEnumerable<DictionaryEntryStaging> entries,
            CancellationToken ct)
        {
            if (entries == null)
                return;

            var list = entries.ToList();
            if (list.Count == 0)
                return;

            var now = DateTime.UtcNow;

            // ✅ FIX: dedupe must align with SQL unique key (UX_Staging_Dedup)
            var deduped = DedupeStagingEntries(list);

            var sanitized = deduped.Select(e =>
                    new DictionaryEntryStaging
                    {
                        Word = SafeTruncate(e.Word, 200),
                        NormalizedWord = SafeTruncate(e.NormalizedWord, 200),
                        PartOfSpeech = SafeTruncate(e.PartOfSpeech, 50),
                        Definition = SafeTruncate(e.Definition, 2000),
                        Etymology = SafeTruncate(e.Etymology, 4000),
                        RawFragment = SafeTruncate(e.RawFragment, 8000),
                        SenseNumber = e.SenseNumber,
                        SourceCode = SafeTruncate(string.IsNullOrWhiteSpace(e.SourceCode) ? "UNKNOWN" : e.SourceCode, 30),
                        CreatedUtc = e.CreatedUtc < SqlMinDate
                            ? now
                            : e.CreatedUtc
                    })
                .ToList();

            if (sanitized.Count == 0)
                return;

            const string sql = """
                               INSERT INTO dbo.DictionaryEntry_Staging (
                                   Word, NormalizedWord, PartOfSpeech, Definition,
                                   Etymology, SenseNumber, SourceCode, CreatedUtc,
                                   RawFragment
                               ) VALUES (
                                   @Word, @NormalizedWord, @PartOfSpeech, @Definition,
                                   @Etymology, @SenseNumber, @SourceCode, @CreatedUtc,
                                   @RawFragment
                               );
                               """;

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                await conn.ExecuteAsync(
                    sql,
                    sanitized,
                    tx);

                await tx.CommitAsync(ct);

                var withRawFragment = sanitized.Count(e => !string.IsNullOrWhiteSpace(e.RawFragment));
                logger.LogInformation(
                    "Committed batch of {Count} staging rows | WithRawFragment={WithRaw} | DedupedFrom={OriginalCount}",
                    sanitized.Count,
                    withRawFragment,
                    list.Count);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);

                var withRawFragment = sanitized.Count(e => !string.IsNullOrWhiteSpace(e.RawFragment));
                logger.LogError(
                    ex,
                    "Rolled back staging batch | AttemptedRows={Count} | WithRawFragment={WithRaw} | DedupedFrom={OriginalCount}",
                    sanitized.Count,
                    withRawFragment,
                    list.Count);

                // ✅ Rule: importer should NEVER crash
            }
        }

        private static List<DictionaryEntryStaging> DedupeStagingEntries(List<DictionaryEntryStaging> entries)
        {
            if (entries == null || entries.Count == 0)
                return new List<DictionaryEntryStaging>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<DictionaryEntryStaging>(entries.Count);

            foreach (var e in entries)
            {
                if (e == null)
                    continue;

                var key = BuildStagingKey(e);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (seen.Add(key))
                    result.Add(e);
            }

            return result;
        }

        // ✅ FIXED: key must match UX_Staging_Dedup behavior (Source + Sense + WordHash + DefHash)
        private static string BuildStagingKey(DictionaryEntryStaging e)
        {
            var source = (e.SourceCode ?? string.Empty).Trim().ToLowerInvariant();

            // SenseNumber is part of unique index (based on duplicate key dump: (KAIKKI, 1, ...))
            var sense = e.SenseNumber;

            // Use NormalizedWord if present, else Word
            var word = NormalizeForKey(string.IsNullOrWhiteSpace(e.NormalizedWord) ? e.Word : e.NormalizedWord);

            // Definition is part of unique index
            var def = NormalizeForKey(e.Definition);

            if (string.IsNullOrWhiteSpace(source))
                source = "unknown";

            if (string.IsNullOrWhiteSpace(word))
                return string.Empty;

            if (string.IsNullOrWhiteSpace(def))
                return string.Empty;

            // IMPORTANT:
            // Do NOT include PartOfSpeech / Etymology / RawFragment in key,
            // because SQL unique index is rejecting duplicates based on hashes that don't include those fields.
            return $"{source}|{sense}|{word}|{def}";
        }

        private static string NormalizeForKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var t = text.Trim().ToLowerInvariant();

            // collapse whitespace
            t = string.Join(" ",
                t.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            // keep key stable but small
            if (t.Length > 512)
                t = t.Substring(0, 512);

            return t;
        }

        private static string? SafeTruncate(string? text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (maxLen < 10)
                return text.Trim();

            var t = text.Trim();
            if (t.Length <= maxLen)
                return t;

            return t.Substring(0, maxLen).Trim();
        }
    }
}
