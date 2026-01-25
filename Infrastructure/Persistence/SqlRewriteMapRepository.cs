using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Rewrite;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlRewriteMapRepository(
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlRewriteMapRepository> logger)
    : IRewriteMapRepository
{
    private readonly ISqlStoredProcedureExecutor _sp = sp;
    private readonly ILogger<SqlRewriteMapRepository> _logger = logger;

    public async Task<IReadOnlyList<RewriteMapRule>> GetRewriteRulesAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct)
    {
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        try
        {
            var rows = await _sp.QueryAsync<RewriteMapRule>(
                "sp_RewriteMap_GetData",
                new
                {
                    Tag = "RULES",
                    ModeCode = ModeToModeCode(mode)
                },
                ct,
                timeoutSeconds: 60);

            if (rows.Count == 0)
                return Array.Empty<RewriteMapRule>();

            return rows
                .Where(r => r is not null)
                .Where(r => r.Enabled)
                .Select(Helper.SqlRepository.NormalizeRewriteRuleOrNull)
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
        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        try
        {
            var rows = await _sp.QueryAsync<string>(
                "sp_RewriteMap_GetData",
                new
                {
                    Tag = "STOPWORDS",
                    ModeCode = (string?)null
                },
                ct,
                timeoutSeconds: 60);

            if (rows.Count == 0)
                return Array.Empty<string>();

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

    private static string ModeToModeCode(RewriteTargetMode mode)
    {
        // rewrite rules are style-based, currently default = English
        return "English";
    }
}