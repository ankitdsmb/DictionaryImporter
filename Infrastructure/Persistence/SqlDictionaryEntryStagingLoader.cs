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

        // NEW METHOD (added)
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

        // NEW METHOD (added)
        private static string BuildStagingKey(DictionaryEntryStaging e)
        {
            var word = (e.Word ?? string.Empty).Trim().ToLowerInvariant();
            var normalizedWord = (e.NormalizedWord ?? string.Empty).Trim().ToLowerInvariant();
            var pos = (e.PartOfSpeech ?? string.Empty).Trim().ToLowerInvariant();
            var source = (e.SourceCode ?? string.Empty).Trim().ToLowerInvariant();
            var sense = e.SenseNumber; // ✅ int, not nullable

            var def = NormalizeForKey(e.Definition);
            var ety = NormalizeForKey(e.Etymology);

            if (string.IsNullOrWhiteSpace(word) && string.IsNullOrWhiteSpace(normalizedWord))
                return string.Empty;

            return $"{source}|{word}|{normalizedWord}|{pos}|{sense}|{def}|{ety}";
        }

        // NEW METHOD (added)
        private static string NormalizeForKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var t = text.Trim().ToLowerInvariant();
            t = string.Join(" ", t.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

            if (t.Length > 256)
                t = t.Substring(0, 256);

            return t;
        }

        // NEW METHOD (added)
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
