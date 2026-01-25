using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Domain.Rewrite
{
    public sealed class RewriteMapPromotionService(
        string connectionString,
        IRewriteMapCandidateRepository candidateRepository,
        ILogger<RewriteMapPromotionService> logger)
    {
        private readonly string _connectionString = connectionString;
        private readonly IRewriteMapCandidateRepository _candidateRepository = candidateRepository;
        private readonly ILogger<RewriteMapPromotionService> _logger = logger;

        public async Task PromoteApprovedAsync(
            string sourceCode,
            int take,
            string promotedBy,
            CancellationToken ct)
        {
            // SourceCode no longer used for rule storage, but kept for filtering candidates + logging (signature unchanged).
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();
            promotedBy = string.IsNullOrWhiteSpace(promotedBy) ? "SYSTEM" : promotedBy.Trim();

            if (take <= 0) take = 1;
            if (take > 5000) take = 5000;

            var approved = await _candidateRepository.GetApprovedCandidatesAsync(sourceCode, take, ct);
            if (approved.Count == 0)
            {
                _logger.LogInformation("No approved rewrite candidates found. SourceCode={SourceCode}", sourceCode);
                return;
            }

            var idsToMark = new List<long>(approved.Count);

            // NEW SCHEMA:
            // Promote into dbo.RewriteRule (single-table).
            // ModeCode:
            //   NULL => global rule
            //   'English', 'Technical', etc => mode-scoped rule
            const string upsertRewriteRuleSql = @"
SET NOCOUNT ON;

DECLARE @NowUtc DATETIME2(3) = SYSUTCDATETIME();

MERGE dbo.RewriteRule WITH (HOLDLOCK) AS target
USING (
    SELECT
        @FromText     AS FromText,
        @ToText       AS ToText,
        @Enabled      AS Enabled,
        @Priority     AS Priority,
        @IsWholeWord  AS IsWholeWord,
        @IsRegex      AS IsRegex,
        @ModeCode     AS ModeCode,
        @Notes        AS Notes
) AS source
ON (
       ISNULL(target.ModeCode, '') = ISNULL(source.ModeCode, '')
   AND target.FromText = source.FromText
   AND target.IsWholeWord = source.IsWholeWord
   AND target.IsRegex = source.IsRegex
)
WHEN MATCHED THEN
    UPDATE SET
        target.ToText = source.ToText,
        target.Enabled = source.Enabled,
        target.Priority = source.Priority,
        target.Notes = source.Notes,
        target.UpdatedUtc = @NowUtc
WHEN NOT MATCHED THEN
    INSERT (FromText, ToText, Enabled, Priority, IsWholeWord, IsRegex, ModeCode, Notes, CreatedUtc, UpdatedUtc)
    VALUES (source.FromText, source.ToText, source.Enabled, source.Priority, source.IsWholeWord, source.IsRegex, source.ModeCode, source.Notes, @NowUtc, @NowUtc);

-- Return RewriteRuleId deterministically
SELECT TOP (1)
    RewriteRuleId
FROM dbo.RewriteRule
WHERE FromText = @FromText
  AND IsWholeWord = @IsWholeWord
  AND IsRegex = @IsRegex
  AND (
        (ModeCode IS NULL AND @ModeCode IS NULL)
     OR (ModeCode = @ModeCode)
  )
ORDER BY RewriteRuleId ASC;";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var tx = conn.BeginTransaction();

                foreach (var c in approved)
                {
                    ct.ThrowIfCancellationRequested();

                    var normalizedMode = NormalizeModeCode(c.Mode);

                    // IMPORTANT:
                    // For RewriteRule we support BOTH:
                    // 1) mode-specific rule => ModeCode = normalizedMode
                    // 2) global rule       => ModeCode = NULL
                    //
                    // Current behavior of promotion service: promote per-mode
                    // because candidate is generated under a mode context.
                    var modeCode = string.IsNullOrWhiteSpace(normalizedMode) ? "English" : normalizedMode;

                    var from = (c.FromText ?? string.Empty).Trim();
                    var to = (c.ToText ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(from)) continue;
                    if (string.IsNullOrWhiteSpace(to)) continue;
                    if (string.Equals(from, to, StringComparison.Ordinal)) continue;

                    // deterministic defaults for promoted rules
                    var priority = ComputePriority(c.SuggestedCount, c.AvgConfidenceScore);

                    // Apply deterministic guard rails so we never exceed schema limits
                    if (from.Length > 400) from = from.Substring(0, 400);
                    if (to.Length > 400) to = to.Substring(0, 400);

                    var rewriteRuleId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                        upsertRewriteRuleSql,
                        new
                        {
                            FromText = from,
                            ToText = to,
                            Enabled = true,
                            Priority = priority,
                            IsWholeWord = true,
                            IsRegex = false,
                            ModeCode = modeCode, // mode-scoped rule
                            Notes = BuildPromotionNotes(promotedBy, sourceCode)
                        },
                        transaction: tx,
                        cancellationToken: ct));

                    if (rewriteRuleId <= 0)
                        continue;

                    idsToMark.Add(c.RewriteMapCandidateId);
                }

                tx.Commit();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("RewriteRule promotion cancelled. SourceCode={SourceCode}", sourceCode);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PromoteApprovedAsync failed. SourceCode={SourceCode}", sourceCode);
                return;
            }

            if (idsToMark.Count > 0)
            {
                await _candidateRepository.MarkPromotedAsync(idsToMark, promotedBy, ct);

                _logger.LogInformation(
                    "RewriteRule promotion complete. SourceCode={SourceCode}, Promoted={Count}",
                    sourceCode,
                    idsToMark.Count);
            }
        }

        // NEW METHOD (added)
        private static string NormalizeModeCode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return string.Empty;

            mode = mode.Trim();

            // Accept exact already-valid mode codes
            if (IsValidModeCode(mode))
                return mode;

            // Backward compatibility: older values that sometimes appeared in candidates/logs
            if (string.Equals(mode, "Definition", StringComparison.OrdinalIgnoreCase))
                return "English";

            if (string.Equals(mode, "MeaningTitle", StringComparison.OrdinalIgnoreCase))
                return "English";

            if (string.Equals(mode, "Title", StringComparison.OrdinalIgnoreCase))
                return "English";

            if (string.Equals(mode, "Example", StringComparison.OrdinalIgnoreCase))
                return "English";

            // Fallback safe default
            return "English";
        }

        // NEW METHOD (added)
        private static bool IsValidModeCode(string modeCode)
        {
            return modeCode.Equals("Academic", StringComparison.Ordinal)
                   || modeCode.Equals("Casual", StringComparison.Ordinal)
                   || modeCode.Equals("Educational", StringComparison.Ordinal)
                   || modeCode.Equals("Email", StringComparison.Ordinal)
                   || modeCode.Equals("English", StringComparison.Ordinal)
                   || modeCode.Equals("Formal", StringComparison.Ordinal)
                   || modeCode.Equals("GrammarFix", StringComparison.Ordinal)
                   || modeCode.Equals("Legal", StringComparison.Ordinal)
                   || modeCode.Equals("Medical", StringComparison.Ordinal)
                   || modeCode.Equals("Neutral", StringComparison.Ordinal)
                   || modeCode.Equals("Professional", StringComparison.Ordinal)
                   || modeCode.Equals("Simplify", StringComparison.Ordinal)
                   || modeCode.Equals("Technical", StringComparison.Ordinal);
        }

        // NEW METHOD (added)
        private static string BuildPromotionNotes(string promotedBy, string sourceCode)
        {
            // Keep deterministic + bounded length to fit Notes NVARCHAR(200)
            var notes = $"PROMOTED_BY={promotedBy};SRC={sourceCode};UTC={DateTime.UtcNow:yyyy-MM-dd}";
            if (notes.Length > 200)
                notes = notes.Substring(0, 200);

            return notes;
        }

        // NEW METHOD (added)
        private static int ComputePriority(int suggestedCount, decimal avgConfidence)
        {
            if (suggestedCount <= 0) suggestedCount = 1;
            if (avgConfidence < 0) avgConfidence = 0;
            if (avgConfidence > 1) avgConfidence = 1;

            var boost = 0;

            if (suggestedCount >= 50) boost += 30;
            else if (suggestedCount >= 10) boost += 20;
            else if (suggestedCount >= 3) boost += 10;

            if (avgConfidence >= 0.9m) boost += 30;
            else if (avgConfidence >= 0.75m) boost += 20;
            else if (avgConfidence >= 0.6m) boost += 10;

            var basePriority = 500;
            var priority = basePriority - boost;

            if (priority < 50) priority = 50;
            if (priority > 1000) priority = 1000;

            return priority;
        }
    }
}
