using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Domain.Rewrite
{
    public sealed class SqlRewriteMapCandidateRepository(
        string connectionString,
        ILogger<SqlRewriteMapCandidateRepository> logger)
        : IRewriteMapCandidateRepository
    {
        private readonly string _connectionString = connectionString;
        private readonly ILogger<SqlRewriteMapCandidateRepository> _logger = logger;

        public async Task UpsertCandidatesAsync(
            IReadOnlyList<RewriteMapCandidateUpsert> candidates,
            CancellationToken ct)
        {
            if (candidates is null || candidates.Count == 0)
                return;

            const string sql = @"
MERGE dbo.RewriteMapCandidate WITH (HOLDLOCK) AS target
USING (
    SELECT
        @SourceCode AS SourceCode,
        @Mode AS Mode,
        @FromText AS FromText,
        @ToText AS ToText,
        @Confidence AS Confidence
) AS source
ON target.SourceCode = source.SourceCode
   AND target.Mode = source.Mode
   AND target.FromText = source.FromText
   AND target.ToText = source.ToText
WHEN MATCHED AND target.Status = 'Pending' THEN
    UPDATE SET
        target.SuggestedCount = target.SuggestedCount + 1,
        target.LastSeenUtc = SYSUTCDATETIME(),
        target.AvgConfidenceScore =
            CASE
                WHEN target.SuggestedCount <= 0 THEN source.Confidence
                ELSE ((target.AvgConfidenceScore * target.SuggestedCount) + source.Confidence) / (target.SuggestedCount + 1)
            END
WHEN NOT MATCHED THEN
    INSERT
    (
        SourceCode,
        Mode,
        FromText,
        ToText,
        SuggestedCount,
        FirstSeenUtc,
        LastSeenUtc,
        AvgConfidenceScore,
        Status
    )
    VALUES
    (
        source.SourceCode,
        source.Mode,
        source.FromText,
        source.ToText,
        1,
        SYSUTCDATETIME(),
        SYSUTCDATETIME(),
        source.Confidence,
        'Pending'
    );";

            try
            {
                var aggregated = AggregateCandidates(candidates);
                if (aggregated.Count == 0)
                    return;

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var tx = conn.BeginTransaction();

                foreach (var c in aggregated)
                {
                    ct.ThrowIfCancellationRequested();

                    await conn.ExecuteAsync(new CommandDefinition(
                        sql,
                        new
                        {
                            SourceCode = c.SourceCode,
                            Mode = c.Mode,
                            FromText = c.FromText,
                            ToText = c.ToText,
                            Confidence = c.Confidence
                        },
                        transaction: tx,
                        cancellationToken: ct,
                        commandTimeout: 60));
                }

                tx.Commit();
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
            sourceCode = NormalizeSourceCode(sourceCode);

            take = take <= 0 ? 200 : take;
            if (take > 2000) take = 2000;

            const string sql = @"
SELECT TOP (@Take)
    RewriteMapCandidateId,
    SourceCode,
    Mode,
    FromText,
    ToText,
    SuggestedCount,
    AvgConfidenceScore
FROM dbo.RewriteMapCandidate WITH (NOLOCK)
WHERE SourceCode = @SourceCode
  AND Status = 'Approved'
ORDER BY RewriteMapCandidateId ASC;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows = await conn.QueryAsync<RewriteMapCandidateRow>(new CommandDefinition(
                    sql,
                    new { SourceCode = sourceCode, Take = take },
                    cancellationToken: ct,
                    commandTimeout: 60));

                return rows.AsList()
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

            approvedBy = string.IsNullOrWhiteSpace(approvedBy) ? "SYSTEM" : approvedBy.Trim();
            if (approvedBy.Length > 128)
                approvedBy = approvedBy.Substring(0, 128);

            var ids = candidateIds.Where(x => x > 0).Distinct().OrderBy(x => x).ToArray();
            if (ids.Length == 0)
                return;

            const string sql = @"
UPDATE dbo.RewriteMapCandidate
SET Status = 'Promoted',
    ApprovedBy = @ApprovedBy,
    ApprovedUtc = SYSUTCDATETIME()
WHERE RewriteMapCandidateId IN @Ids;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                await conn.ExecuteAsync(new CommandDefinition(
                    sql,
                    new { ApprovedBy = approvedBy, Ids = ids },
                    cancellationToken: ct,
                    commandTimeout: 60));
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

        // NEW METHOD (added)
        public async Task<IReadOnlySet<string>> GetExistingRewriteMapKeysAsync(
            string sourceCode,
            CancellationToken ct)
        {
            _ = NormalizeSourceCode(sourceCode);

            const string sql = @"
SELECT
    ISNULL(r.ModeCode, '') AS ModeCode,
    r.FromText
FROM dbo.RewriteRule r WITH (NOLOCK)
WHERE r.Enabled = 1
  AND r.FromText IS NOT NULL
  AND LTRIM(RTRIM(r.FromText)) <> '';";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows = await conn.QueryAsync<RewriteMapKeyRow>(new CommandDefinition(
                    sql,
                    cancellationToken: ct,
                    commandTimeout: 60));

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

        // NEW METHOD (added)
        private static List<RewriteMapCandidateUpsert> AggregateCandidates(IReadOnlyList<RewriteMapCandidateUpsert> candidates)
        {
            var map = new Dictionary<string, CandidateAccumulator>(StringComparer.Ordinal);

            foreach (var c in candidates)
            {
                if (c is null)
                    continue;

                var source = NormalizeSourceCode(c.SourceCode);
                var mode = NormalizeModeCode(c.Mode);
                var from = (c.FromText ?? string.Empty).Trim();
                var to = (c.ToText ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(source)) continue;
                if (string.IsNullOrWhiteSpace(mode)) continue;
                if (string.IsNullOrWhiteSpace(from)) continue;
                if (string.IsNullOrWhiteSpace(to)) continue;
                if (string.Equals(from, to, StringComparison.Ordinal)) continue;

                if (from.Length > 400) from = from.Substring(0, 400);
                if (to.Length > 400) to = to.Substring(0, 400);

                var confidence = NormalizeConfidence(c.Confidence);

                var key = string.Concat(source, "|", mode, "|", from, "|", to);

                if (map.TryGetValue(key, out var acc))
                {
                    // deterministic: keep max confidence observed
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

        // NEW METHOD (added)
        private static string NormalizeSourceCode(string? sourceCode)
        {
            return string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();
        }

        // NEW METHOD (added)
        private static decimal NormalizeConfidence(decimal confidence)
        {
            if (confidence < 0) return 0;
            if (confidence > 1) return 1;
            return confidence;
        }

        // NEW METHOD (added)
        private static string NormalizeModeCode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return string.Empty;

            mode = mode.Trim();

            var normalized = NormalizeModeCodeCore(mode);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;

            if (mode.Equals("Definition", StringComparison.OrdinalIgnoreCase)) return "English";
            if (mode.Equals("MeaningTitle", StringComparison.OrdinalIgnoreCase)) return "English";
            if (mode.Equals("Title", StringComparison.OrdinalIgnoreCase)) return "English";
            if (mode.Equals("Example", StringComparison.OrdinalIgnoreCase)) return "English";

            return "English";
        }

        // NEW METHOD (added)
        private static string NormalizeModeCodeCore(string mode)
        {
            if (mode.Equals("Academic", StringComparison.OrdinalIgnoreCase)) return "Academic";
            if (mode.Equals("Casual", StringComparison.OrdinalIgnoreCase)) return "Casual";
            if (mode.Equals("Educational", StringComparison.OrdinalIgnoreCase)) return "Educational";
            if (mode.Equals("Email", StringComparison.OrdinalIgnoreCase)) return "Email";
            if (mode.Equals("English", StringComparison.OrdinalIgnoreCase)) return "English";
            if (mode.Equals("Formal", StringComparison.OrdinalIgnoreCase)) return "Formal";
            if (mode.Equals("GrammarFix", StringComparison.OrdinalIgnoreCase)) return "GrammarFix";
            if (mode.Equals("Legal", StringComparison.OrdinalIgnoreCase)) return "Legal";
            if (mode.Equals("Medical", StringComparison.OrdinalIgnoreCase)) return "Medical";
            if (mode.Equals("Neutral", StringComparison.OrdinalIgnoreCase)) return "Neutral";
            if (mode.Equals("Professional", StringComparison.OrdinalIgnoreCase)) return "Professional";
            if (mode.Equals("Simplify", StringComparison.OrdinalIgnoreCase)) return "Simplify";
            if (mode.Equals("Technical", StringComparison.OrdinalIgnoreCase)) return "Technical";
            return string.Empty;
        }

        // NEW METHOD (added)
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
