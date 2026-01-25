using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Core.Rewrite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlRewriteMapRepository(
        string connectionString,
        ILogger<SqlRewriteMapRepository> logger)
        : IRewriteMapRepository
    {
        private readonly string _connectionString = connectionString;
        private readonly ILogger<SqlRewriteMapRepository> _logger = logger;

        public async Task<IReadOnlyList<RewriteMapRule>> GetRewriteRulesAsync(
            string sourceCode,
            RewriteTargetMode mode,
            CancellationToken ct)
        {
            // NOTE:
            // RewriteMap+RewriteMapMode was replaced by dbo.RewriteRule (single table).
            // Signature still has sourceCode for compatibility (caller unchanged).
            sourceCode = NormalizeSource(sourceCode);

            const string sql = """
SELECT
    r.RewriteRuleId AS RewriteMapId,
    r.FromText      AS FromText,
    r.ToText        AS ToText,
    r.IsWholeWord   AS WholeWord,
    r.IsRegex       AS IsRegex,
    r.Priority      AS Priority,
    r.Enabled       AS Enabled
FROM dbo.RewriteRule r WITH (NOLOCK)
WHERE
    r.Enabled = 1
    AND (r.ModeCode IS NULL OR r.ModeCode = @ModeCode)
ORDER BY
    r.Priority ASC,
    LEN(r.FromText) DESC,
    r.FromText ASC,
    r.RewriteRuleId ASC;
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows = await conn.QueryAsync<RewriteMapRule>(
                    new CommandDefinition(
                        sql,
                        new { ModeCode = ModeToModeCode(mode) },
                        cancellationToken: ct,
                        commandTimeout: 60));

                var list = rows.AsList();

                // Defensive filtering (never crash)
                return list
                    .Where(r => r is not null)
                    .Where(r => r.Enabled)
                    .Where(r => !string.IsNullOrWhiteSpace(r.FromText))
                    .Select(NormalizeRule)
                    .Where(r => r is not null)
                    .Select(r => r!)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<RewriteMapRule>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GetRewriteRulesAsync failed. Source={Source}, Mode={Mode}",
                    sourceCode, mode);

                return Array.Empty<RewriteMapRule>();
            }
        }

        public async Task<IReadOnlyList<string>> GetStopWordsAsync(
            string sourceCode,
            RewriteTargetMode mode,
            CancellationToken ct)
        {
            // IMPORTANT:
            // You did NOT provide RewriteStopWord schema migration.
            // So we assume RewriteStopWord still contains legacy Mode column.
            sourceCode = NormalizeSource(sourceCode);

            const string sql = """
SELECT
    sw.Word
FROM dbo.RewriteStopWord sw WITH (NOLOCK)
WHERE
    sw.Enabled = 1
    --AND sw.Mode = @Mode
ORDER BY
    LEN(sw.Word) DESC,
    sw.Word ASC;
""";

            try
            {
                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct);

                var rows = await conn.QueryAsync<string>(
                    new CommandDefinition(
                        sql,
                        new { Mode = ModeToLegacyMode(mode) },
                        cancellationToken: ct,
                        commandTimeout: 60));

                return rows
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GetStopWordsAsync failed. Source={Source}, Mode={Mode}",
                    sourceCode, mode);

                return Array.Empty<string>();
            }
        }

        // NEW METHOD (added)
        private static RewriteMapRule? NormalizeRule(RewriteMapRule? r)
        {
            if (r is null) return null;

            r.FromText = (r.FromText ?? string.Empty).Trim();
            r.ToText = (r.ToText ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(r.FromText))
                return null;

            // deterministic safe defaults
            if (r.Priority <= 0)
                r.Priority = 100;

            return r;
        }

        // NEW METHOD (added)
        private static string NormalizeSource(string? sourceCode)
            => string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

        // NEW METHOD (added)
        private static string ModeToModeCode(RewriteTargetMode mode)
        {
            // Mapping rewrite target mode -> RewriteRule.ModeCode "style mode".
            //
            // Current system default = "English"
            // (global rules are ModeCode NULL and always included)
            return "English";
        }

        // NEW METHOD (added)
        private static string ModeToLegacyMode(RewriteTargetMode mode) =>
            mode switch
            {
                RewriteTargetMode.Definition => "Definition",
                RewriteTargetMode.Example => "Example",
                RewriteTargetMode.Title => "Title",
                _ => "Definition"
            };
    }
}
