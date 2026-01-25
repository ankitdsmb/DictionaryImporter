using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Gateway.Rewriter
{
    public sealed class SqlRewriteRuleHitRepository(
        string connectionString,
        ILogger<SqlRewriteRuleHitRepository> logger)
        : IRewriteRuleHitRepository
    {
        private readonly string _connectionString = connectionString;
        private readonly ILogger<SqlRewriteRuleHitRepository> _logger = logger;

        public async Task UpsertHitsAsync(
            IReadOnlyList<RewriteRuleHitUpsert> hits,
            CancellationToken ct)
        {
            if (hits is null || hits.Count == 0)
                return;

            const string sql = @"
MERGE dbo.RewriteRuleHitLog WITH (HOLDLOCK) AS target
USING (
    SELECT
        @SourceCode AS SourceCode,
        @Mode AS Mode,
        @RuleType AS RuleType,
        @RuleKey AS RuleKey,
        @HitCount AS HitCount
) AS source
ON target.SourceCode = source.SourceCode
   AND target.Mode = source.Mode
   AND target.RuleType = source.RuleType
   AND target.RuleKey = source.RuleKey
WHEN MATCHED THEN
    UPDATE SET
        target.HitCount = target.HitCount + source.HitCount,
        target.LastHitUtc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (SourceCode, Mode, RuleType, RuleKey, HitCount, FirstHitUtc, LastHitUtc)
    VALUES (source.SourceCode, source.Mode, source.RuleType, source.RuleKey, source.HitCount, SYSUTCDATETIME(), SYSUTCDATETIME());";

            try
            {
                var aggregated = AggregateHits(hits);
                if (aggregated.Count == 0)
                    return;

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted);

                foreach (var h in aggregated)
                {
                    ct.ThrowIfCancellationRequested();

                    await conn.ExecuteAsync(new CommandDefinition(
                        sql,
                        new
                        {
                            SourceCode = h.SourceCode,
                            Mode = h.Mode,
                            RuleType = h.RuleType,
                            RuleKey = h.RuleKey,
                            HitCount = h.HitCount
                        },
                        transaction: tx,
                        cancellationToken: ct,
                        commandTimeout: 60));
                }

                tx.Commit();
            }
            catch (OperationCanceledException)
            {
                // normal cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpsertHitsAsync failed.");
            }
        }

        private static List<RewriteRuleHitUpsert> AggregateHits(IReadOnlyList<RewriteRuleHitUpsert> hits)
        {
            var map = new Dictionary<string, HitAccumulator>(StringComparer.Ordinal);

            foreach (var h in hits)
            {
                if (h is null)
                    continue;

                var source = (h.SourceCode ?? string.Empty).Trim();
                var mode = (h.Mode ?? string.Empty).Trim();
                var ruleType = (h.RuleType ?? string.Empty).Trim();
                var ruleKey = (h.RuleKey ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(source)) continue;
                if (string.IsNullOrWhiteSpace(mode)) continue;
                if (string.IsNullOrWhiteSpace(ruleType)) continue;
                if (string.IsNullOrWhiteSpace(ruleKey)) continue;

                if (ruleKey.Length > 400)
                    ruleKey = ruleKey.Substring(0, 400);

                var count = h.HitCount <= 0 ? 1 : h.HitCount;
                var key = string.Concat(source, "|", mode, "|", ruleType, "|", ruleKey);

                if (map.TryGetValue(key, out var acc))
                {
                    acc.TotalHits = AddSafe(acc.TotalHits, count);
                    map[key] = acc;
                }
                else
                {
                    map[key] = new HitAccumulator
                    {
                        SourceCode = source,
                        Mode = mode,
                        RuleType = ruleType,
                        RuleKey = ruleKey,
                        TotalHits = count
                    };
                }
            }

            var result = new List<RewriteRuleHitUpsert>(map.Count);

            foreach (var kv in map)
            {
                var acc = kv.Value;

                result.Add(new RewriteRuleHitUpsert
                {
                    SourceCode = acc.SourceCode,
                    Mode = acc.Mode,
                    RuleType = acc.RuleType,
                    RuleKey = acc.RuleKey,
                    HitCount = ClampToInt(acc.TotalHits)
                });
            }

            result.Sort(static (a, b) =>
            {
                var c = string.CompareOrdinal(a.SourceCode, b.SourceCode);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.Mode, b.Mode);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.RuleType, b.RuleType);
                if (c != 0) return c;
                return string.CompareOrdinal(a.RuleKey, b.RuleKey);
            });

            return result;
        }

        private static long AddSafe(long a, long b)
        {
            if (b > 0 && a > long.MaxValue - b) return long.MaxValue;
            if (b < 0 && a < long.MinValue - b) return long.MinValue;
            return a + b;
        }

        private static int ClampToInt(long value)
        {
            if (value <= 0) return 1;
            if (value > int.MaxValue) return int.MaxValue;
            return (int)value;
        }

        private struct HitAccumulator
        {
            public string SourceCode;
            public string Mode;
            public string RuleType;
            public string RuleKey;
            public long TotalHits;
        }
    }
}
