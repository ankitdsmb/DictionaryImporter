using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Core.Abstractions.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlExampleAiEnhancementRepository(
        string connectionString,
        ILogger<SqlExampleAiEnhancementRepository> logger)
        : IExampleAiEnhancementRepository
    {
        private const string PlaceholderNonEnglish = "[NON_ENGLISH]";
        private const string PlaceholderBilingual = "[BILINGUAL_EXAMPLE]";

        private const int DefaultTake = 1000;
        private const int MaxTakeHardLimit = 5000;

        private readonly string _connectionString = connectionString;
        private readonly ILogger<SqlExampleAiEnhancementRepository> _logger = logger;

        public async Task<IReadOnlyList<ExampleRewriteCandidate>> GetExampleCandidatesAsync(
            string sourceCode,
            int take,
            bool forceRewrite,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            if (take <= 0) take = DefaultTake;
            if (take > MaxTakeHardLimit) take = MaxTakeHardLimit;

            var sql = forceRewrite
                ? """
SELECT TOP (@Take)
    ex.DictionaryEntryExampleId,
    ex.DictionaryEntryParsedId,
    ISNULL(ex.ExampleText, '') AS ExampleText
FROM dbo.DictionaryEntryExample ex WITH (NOLOCK)
WHERE
    ex.SourceCode = @SourceCode
    AND ex.ExampleText IS NOT NULL
    AND LTRIM(RTRIM(ex.ExampleText)) <> ''

    AND ISNULL(ex.HasNonEnglishText, 0) = 0
    AND ex.NonEnglishTextId IS NULL

    AND ex.ExampleText <> '[NON_ENGLISH]'
    AND ex.ExampleText <> '[BILINGUAL_EXAMPLE]'
ORDER BY
    ex.DictionaryEntryExampleId ASC;
"""
                : """
SELECT TOP (@Take)
    ex.DictionaryEntryExampleId,
    ex.DictionaryEntryParsedId,
    ISNULL(ex.ExampleText, '') AS ExampleText
FROM dbo.DictionaryEntryExample ex WITH (NOLOCK)
WHERE
    ex.SourceCode = @SourceCode
    AND ex.ExampleText IS NOT NULL
    AND LTRIM(RTRIM(ex.ExampleText)) <> ''

    AND ISNULL(ex.HasNonEnglishText, 0) = 0
    AND ex.NonEnglishTextId IS NULL

    AND ISNULL(ex.AiEnhanced, 0) = 0
    AND (ex.AiEnhancedExample IS NULL OR LTRIM(RTRIM(ex.AiEnhancedExample)) = '')

    AND ex.ExampleText <> '[NON_ENGLISH]'
    AND ex.ExampleText <> '[BILINGUAL_EXAMPLE]'
ORDER BY
    ex.DictionaryEntryExampleId ASC;
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows = await conn.QueryAsync<ExampleRewriteCandidate>(
                    new CommandDefinition(
                        sql,
                        new { SourceCode = sourceCode, Take = take },
                        cancellationToken: ct,
                        commandTimeout: 60));

                return rows.AsList()
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
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            if (enhancements is null || enhancements.Count == 0)
                return;

            var sql = forceRewrite
                ? """
UPDATE ex
SET
    ex.OriginalExampleText = @OriginalExampleText,
    ex.AiEnhancedExample = @AiEnhancedExample,
    ex.AiEnhanced = 1,
    ex.AiEnhancedDate = SYSUTCDATETIME(),
    ex.AiModelApplied = @AiModelApplied,
    ex.AiConfidenceScore = @AiConfidenceScore
FROM dbo.DictionaryEntryExample ex
WHERE
    ex.SourceCode = @SourceCode
    AND ex.DictionaryEntryExampleId = @DictionaryEntryExampleId;
"""
                : """
UPDATE ex
SET
    ex.OriginalExampleText = @OriginalExampleText,
    ex.AiEnhancedExample = @AiEnhancedExample,
    ex.AiEnhanced = 1,
    ex.AiEnhancedDate = SYSUTCDATETIME(),
    ex.AiModelApplied = @AiModelApplied,
    ex.AiConfidenceScore = @AiConfidenceScore
FROM dbo.DictionaryEntryExample ex
WHERE
    ex.SourceCode = @SourceCode
    AND ex.DictionaryEntryExampleId = @DictionaryEntryExampleId
    AND ISNULL(ex.AiEnhanced, 0) = 0
    AND (ex.AiEnhancedExample IS NULL OR LTRIM(RTRIM(ex.AiEnhancedExample)) = '');
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                using var tx = conn.BeginTransaction();

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

                    if (ContainsBlockedPlaceholder(original) || ContainsBlockedPlaceholder(rewritten))
                        continue;

                    var modelApplied = string.IsNullOrWhiteSpace(e.Model)
                        ? "Regex+RewriteRule+Humanizer"
                        : e.Model.Trim();

                    if (modelApplied.Length > 128)
                        modelApplied = modelApplied.Substring(0, 128);

                    var confidence = NormalizeConfidence(e.Confidence);

                    await conn.ExecuteAsync(new CommandDefinition(
                        sql,
                        new
                        {
                            SourceCode = sourceCode,
                            DictionaryEntryExampleId = e.DictionaryEntryExampleId,
                            OriginalExampleText = original,
                            AiEnhancedExample = rewritten,
                            AiModelApplied = modelApplied,
                            AiConfidenceScore = confidence
                        },
                        transaction: tx,
                        cancellationToken: ct,
                        commandTimeout: 60));
                }

                tx.Commit();
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

        private static bool ContainsBlockedPlaceholder(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.Contains(PlaceholderNonEnglish, StringComparison.OrdinalIgnoreCase)
                   || text.Contains(PlaceholderBilingual, StringComparison.OrdinalIgnoreCase);
        }

        private static int NormalizeConfidence(int confidence)
        {
            if (confidence <= 0) return 80;
            if (confidence > 100) return 100;
            return confidence;
        }
    }
}
