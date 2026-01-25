using Dapper;
using DictionaryImporter.Common;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlRewriteRuleHitRepository(
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlRewriteRuleHitRepository> logger)
    : IRewriteRuleHitRepository
{
    private readonly ISqlStoredProcedureExecutor _sp = sp;
    private readonly ILogger<SqlRewriteRuleHitRepository> _logger = logger;

    public async Task UpsertHitsAsync(
        IReadOnlyList<RewriteRuleHitUpsert> hits,
        CancellationToken ct)
    {
        if (hits is null || hits.Count == 0)
            return;

        try
        {
            var aggregated = AggregateHits(hits);
            if (aggregated.Count == 0)
                return;

            var tvp = ToRewriteRuleHitBatchTvp(aggregated);

            await _sp.ExecuteAsync(
                "sp_RewriteRuleHitLog_UpsertBatch",
                new { Rows = tvp },
                ct,
                timeoutSeconds: 60);
        }
        catch (OperationCanceledException)
        {
            // normal cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpsertHitsAsync failed (TVP batch).");
        }
    }

    // NEW METHOD (added)
    private static object ToRewriteRuleHitBatchTvp(IEnumerable<RewriteRuleHitUpsert> rows)
    {
        var dt = new DataTable();
        dt.Columns.Add("SourceCode", typeof(string));
        dt.Columns.Add("Mode", typeof(string));
        dt.Columns.Add("RuleType", typeof(string));
        dt.Columns.Add("RuleKey", typeof(string));
        dt.Columns.Add("HitCount", typeof(int));

        foreach (var r in rows)
        {
            if (r is null)
                continue;

            var sourceCode = Helper.SqlRepository.NormalizeSourceCode(r.SourceCode);
            var mode = Helper.SqlRepository.NormalizeString(r.Mode, "English");
            var ruleType = Helper.SqlRepository.NormalizeString(r.RuleType, "RewriteRule");
            var ruleKey = Helper.SqlRepository.Truncate(r.RuleKey, 400);

            if (string.IsNullOrWhiteSpace(sourceCode)) continue;
            if (string.IsNullOrWhiteSpace(mode)) continue;
            if (string.IsNullOrWhiteSpace(ruleType)) continue;
            if (string.IsNullOrWhiteSpace(ruleKey)) continue;

            var hitCount = r.HitCount <= 0 ? 1 : r.HitCount;

            dt.Rows.Add(
                sourceCode,
                mode,
                ruleType,
                ruleKey,
                hitCount);
        }

        return dt.AsTableValuedParameter("dbo.RewriteRuleHitLogBatchType");
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