using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Abstractions.Persistence;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlExampleAiEnhancementRepository(
        ISqlStoredProcedureExecutor sp,
        ILogger<SqlExampleAiEnhancementRepository> logger)
        : IExampleAiEnhancementRepository
    {
        private const int DefaultTake = 1000;
        private const int MaxTakeHardLimit = 5000;

        private readonly ISqlStoredProcedureExecutor _sp = sp;
        private readonly ILogger<SqlExampleAiEnhancementRepository> _logger = logger;

        public async Task<IReadOnlyList<ExampleRewriteCandidate>> GetExampleCandidatesAsync(
            string sourceCode,
            int take,
            bool forceRewrite,
            CancellationToken ct)
        {
            sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

            if (take <= 0) take = DefaultTake;
            if (take > MaxTakeHardLimit) take = MaxTakeHardLimit;

            try
            {
                var rows = forceRewrite
                    ? await _sp.QueryAsync<ExampleRewriteCandidate>(
                        "sp_DictionaryEntryExample_GetRewriteCandidates_Force",
                        new { SourceCode = sourceCode, Take = take },
                        ct,
                        timeoutSeconds: 60)
                    : await _sp.QueryAsync<ExampleRewriteCandidate>(
                        "sp_DictionaryEntryExample_GetRewriteCandidates_PendingOnly",
                        new { SourceCode = sourceCode, Take = take },
                        ct,
                        timeoutSeconds: 60);

                if (rows.Count == 0)
                    return Array.Empty<ExampleRewriteCandidate>();

                return rows
                    .OrderBy(x => x.DictionaryEntryExampleId)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<ExampleRewriteCandidate>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetExampleCandidatesAsync failed. SourceCode={SourceCode}", sourceCode);
                return Array.Empty<ExampleRewriteCandidate>();
            }
        }

        public async Task SaveExampleEnhancementsAsync(
            string sourceCode,
            IReadOnlyList<ExampleRewriteEnhancement> enhancements,
            bool forceRewrite,
            CancellationToken ct)
        {
            sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

            if (enhancements is null || enhancements.Count == 0)
                return;

            try
            {
                var ordered = enhancements
                    .Where(x => x is not null)
                    .Where(x => x.DictionaryEntryExampleId > 0)
                    .OrderBy(x => x.DictionaryEntryExampleId)
                    .ToList();

                foreach (var e in ordered)
                {
                    ct.ThrowIfCancellationRequested();

                    var original = (e.OriginalExampleText ?? string.Empty).Trim();
                    var rewritten = (e.RewrittenExampleText ?? string.Empty).Trim();

                    if (string.IsNullOrWhiteSpace(original)) continue;
                    if (string.IsNullOrWhiteSpace(rewritten)) continue;
                    if (string.Equals(original, rewritten, StringComparison.Ordinal)) continue;

                    if (Helper.SqlRepository.ContainsBlockedExamplePlaceholder(original)
                        || Helper.SqlRepository.ContainsBlockedExamplePlaceholder(rewritten))
                        continue;

                    var modelApplied = string.IsNullOrWhiteSpace(e.Model)
                        ? "Regex+RewriteRule+Humanizer"
                        : e.Model.Trim();

                    if (modelApplied.Length > 128)
                        modelApplied = modelApplied.Substring(0, 128);

                    var confidence = Helper.SqlRepository.NormalizeAiConfidenceOrDefault(e.Confidence, defaultValue: 80);

                    if (forceRewrite)
                    {
                        await _sp.ExecuteAsync(
                            "sp_DictionaryEntryExample_SaveAiEnhancement_Force",
                            new
                            {
                                SourceCode = sourceCode,
                                DictionaryEntryExampleId = e.DictionaryEntryExampleId,
                                OriginalExampleText = original,
                                AiEnhancedExample = rewritten,
                                AiModelApplied = modelApplied,
                                AiConfidenceScore = confidence
                            },
                            ct,
                            timeoutSeconds: 60);
                    }
                    else
                    {
                        await _sp.ExecuteAsync(
                            "sp_DictionaryEntryExample_SaveAiEnhancement_IfNotEnhanced",
                            new
                            {
                                SourceCode = sourceCode,
                                DictionaryEntryExampleId = e.DictionaryEntryExampleId,
                                OriginalExampleText = original,
                                AiEnhancedExample = rewritten,
                                AiModelApplied = modelApplied,
                                AiConfidenceScore = confidence
                            },
                            ct,
                            timeoutSeconds: 60);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("SaveExampleEnhancementsAsync cancelled. SourceCode={SourceCode}", sourceCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveExampleEnhancementsAsync failed. SourceCode={SourceCode}", sourceCode);
            }
        }
    }
}
