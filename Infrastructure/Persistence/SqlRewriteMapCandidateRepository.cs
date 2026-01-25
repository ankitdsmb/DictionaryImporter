using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlRewriteMapCandidateRepository(
        ISqlStoredProcedureExecutor sp,
        ILogger<SqlRewriteMapCandidateRepository> logger)
        : IRewriteMapCandidateRepository
    {
        private readonly ISqlStoredProcedureExecutor _sp = sp;
        private readonly ILogger<SqlRewriteMapCandidateRepository> _logger = logger;

        public async Task UpsertCandidatesAsync(
            IReadOnlyList<RewriteMapCandidateUpsert> candidates,
            CancellationToken ct)
        {
            if (candidates is null || candidates.Count == 0)
                return;

            try
            {
                var aggregated = AggregateCandidates(candidates);
                if (aggregated.Count == 0)
                    return;

                foreach (var c in aggregated)
                {
                    ct.ThrowIfCancellationRequested();

                    await _sp.ExecuteAsync(
                        "sp_RewriteMapCandidate_Upsert",
                        new
                        {
                            c.SourceCode,
                            c.Mode,
                            c.FromText,
                            c.ToText,
                            c.Confidence
                        },
                        ct,
                        timeoutSeconds: 60);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("UpsertCandidatesAsync cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpsertCandidatesAsync failed.");
            }
        }

        public async Task<IReadOnlyList<RewriteMapCandidateRow>> GetApprovedCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct)
        {
            sourceCode = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);

            take = take <= 0 ? 200 : take;
            if (take > 2000) take = 2000;

            try
            {
                var rows = await _sp.QueryAsync<RewriteMapCandidateRow>(
                    "sp_RewriteMapCandidate_GetApproved",
                    new
                    {
                        SourceCode = sourceCode,
                        Take = take
                    },
                    ct,
                    timeoutSeconds: 60);

                return rows
                    .OrderBy(x => x.RewriteMapCandidateId)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<RewriteMapCandidateRow>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetApprovedCandidatesAsync failed.");
                return Array.Empty<RewriteMapCandidateRow>();
            }
        }

        public async Task MarkPromotedAsync(
            IReadOnlyList<long> candidateIds,
            string approvedBy,
            CancellationToken ct)
        {
            if (candidateIds is null || candidateIds.Count == 0)
                return;

            approvedBy = SqlRepositoryHelper.NormalizeString(approvedBy, SqlRepositoryHelper.DefaultPromotedBy);
            approvedBy = SqlRepositoryHelper.Truncate(approvedBy, 128);

            var ids = SqlRepositoryHelper.NormalizeDistinctIds(candidateIds);
            if (ids.Length == 0)
                return;

            try
            {
                var tvp = SqlRepositoryHelper.ToBigIntIdListTvp(ids);

                await _sp.ExecuteAsync(
                    "sp_RewriteMapCandidate_MarkPromoted",
                    new
                    {
                        ApprovedBy = approvedBy,
                        Ids = tvp
                    },
                    ct,
                    timeoutSeconds: 60);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkPromotedAsync failed.");
            }
        }

        public async Task<IReadOnlySet<string>> GetExistingRewriteMapKeysAsync(
            string sourceCode,
            CancellationToken ct)
        {
            _ = SqlRepositoryHelper.NormalizeSourceCode(sourceCode);

            try
            {
                var rows = await _sp.QueryAsync<RewriteMapKeyRow>(
                    "sp_RewriteRule_GetExistingKeys",
                    new { },
                    ct,
                    timeoutSeconds: 60);

                var set = new HashSet<string>(StringComparer.Ordinal);

                foreach (var r in rows)
                {
                    var mode = (r.ModeCode ?? string.Empty).Trim();
                    var from = (r.FromText ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(from))
                        continue;

                    if (string.IsNullOrWhiteSpace(mode))
                        mode = "GLOBAL";

                    set.Add($"{mode}|{from}");
                }

                return set;
            }
            catch (OperationCanceledException)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExistingRewriteMapKeysAsync failed.");
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        private static List<RewriteMapCandidateUpsert> AggregateCandidates(IReadOnlyList<RewriteMapCandidateUpsert> candidates)
        {
            var map = new Dictionary<string, CandidateAccumulator>(StringComparer.Ordinal);

            foreach (var c in candidates)
            {
                if (c is null)
                    continue;

                var source = SqlRepositoryHelper.NormalizeSourceCode(c.SourceCode);
                var mode = SqlRepositoryHelper.NormalizeModeCode(c.Mode);
                var from = (c.FromText ?? string.Empty).Trim();
                var to = (c.ToText ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(source)) continue;
                if (string.IsNullOrWhiteSpace(mode)) continue;
                if (string.IsNullOrWhiteSpace(from)) continue;
                if (string.IsNullOrWhiteSpace(to)) continue;
                if (string.Equals(from, to, StringComparison.Ordinal)) continue;

                from = SqlRepositoryHelper.Truncate(from, 400);
                to = SqlRepositoryHelper.Truncate(to, 400);

                var confidence = SqlRepositoryHelper.NormalizeConfidence01(c.Confidence);

                var key = string.Concat(source, "|", mode, "|", from, "|", to);

                if (map.TryGetValue(key, out var acc))
                {
                    if (confidence > acc.MaxConfidence)
                        acc.MaxConfidence = confidence;

                    map[key] = acc;
                }
                else
                {
                    map[key] = new CandidateAccumulator
                    {
                        SourceCode = source,
                        Mode = mode,
                        FromText = from,
                        ToText = to,
                        MaxConfidence = confidence
                    };
                }
            }

            var result = new List<RewriteMapCandidateUpsert>(map.Count);

            foreach (var kv in map)
            {
                var acc = kv.Value;

                result.Add(new RewriteMapCandidateUpsert
                {
                    SourceCode = acc.SourceCode,
                    Mode = acc.Mode,
                    FromText = acc.FromText,
                    ToText = acc.ToText,
                    Confidence = acc.MaxConfidence
                });
            }

            result.Sort(static (a, b) =>
            {
                var c = string.CompareOrdinal(a.SourceCode, b.SourceCode);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.Mode, b.Mode);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.FromText, b.FromText);
                if (c != 0) return c;
                return string.CompareOrdinal(a.ToText, b.ToText);
            });

            return result;
        }

        private struct CandidateAccumulator
        {
            public string SourceCode;
            public string Mode;
            public string FromText;
            public string ToText;
            public decimal MaxConfidence;
        }

        private sealed class RewriteMapKeyRow
        {
            public string? ModeCode { get; set; }
            public string? FromText { get; set; }
        }
    }
}
