using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Domain.Rewrite;

public sealed class RewriteMapPromotionService(
    string connectionString,
    IRewriteMapCandidateRepository candidateRepository,
    ISqlStoredProcedureExecutor sp,
    ILogger<RewriteMapPromotionService> logger)
{
    private readonly string _connectionString = connectionString;
    private readonly IRewriteMapCandidateRepository _candidateRepository = candidateRepository;
    private readonly ISqlStoredProcedureExecutor _sp = sp;
    private readonly ILogger<RewriteMapPromotionService> _logger = logger;

    public async Task PromoteApprovedAsync(
        string sourceCode,
        int take,
        string promotedBy,
        CancellationToken ct)
    {
        sourceCode =  Helper.SqlRepository.NormalizeSourceCode(sourceCode);
        promotedBy = Helper.SqlRepository.NormalizeString(promotedBy, Helper.SqlRepository.DefaultPromotedBy);

        take = Helper.SqlRepository.Clamp(take <= 0 ? 1 : take, 1, 5000);

        var approved = await _candidateRepository.GetApprovedCandidatesAsync(sourceCode, take, ct);
        if (approved.Count == 0)
        {
            _logger.LogInformation("No approved rewrite candidates found. SourceCode={SourceCode}", sourceCode);
            return;
        }

        var idsToMark = new List<long>(approved.Count);

        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            using var tx = conn.BeginTransaction();

            foreach (var c in approved)
            {
                ct.ThrowIfCancellationRequested();

                var modeCode = Helper.SqlRepository.NormalizeModeCode(c.Mode);

                var from = (c.FromText ?? string.Empty).Trim();
                var to = (c.ToText ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(from)) continue;
                if (string.IsNullOrWhiteSpace(to)) continue;
                if (string.Equals(from, to, StringComparison.Ordinal)) continue;

                var priority = Helper.SqlRepository.ComputePriority(c.SuggestedCount, c.AvgConfidenceScore);

                if (from.Length > 400) from = from.Substring(0, 400);
                if (to.Length > 400) to = to.Substring(0, 400);

                var rewriteRuleId = await _sp.ExecuteScalarAsync<long>(
                    "sp_RewriteRule_Upsert",
                    new
                    {
                        FromText = from,
                        ToText = to,
                        Enabled = true,
                        Priority = priority,
                        IsWholeWord = true,
                        IsRegex = false,
                        ModeCode = modeCode,
                        Notes = Helper.SqlRepository.BuildPromotionNotes(promotedBy, sourceCode)
                    },
                    ct,
                    tx,
                    timeoutSeconds: 60);

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
}