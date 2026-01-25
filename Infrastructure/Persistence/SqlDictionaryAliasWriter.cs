using System;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryAliasWriter(
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlDictionaryAliasWriter> logger)
    : IDictionaryEntryAliasWriter
{
    private readonly ISqlStoredProcedureExecutor _sp =
        sp ?? throw new ArgumentNullException(nameof(sp));

    private readonly ILogger<SqlDictionaryAliasWriter> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task WriteAsync(
        long dictionaryEntryParsedId,
        string alias,
        string sourceCode,
        CancellationToken ct)
    {
        if (dictionaryEntryParsedId <= 0)
            return;

        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        alias = Helper.SqlRepository.NormalizeAliasOrEmpty(alias);
        if (string.IsNullOrWhiteSpace(alias))
            return;

        try
        {
            await _sp.ExecuteAsync(
                "sp_DictionaryEntryAlias_InsertIfMissing",
                new
                {
                    DictionaryEntryParsedId = dictionaryEntryParsedId,
                    AliasText = alias,
                    SourceCode = sourceCode
                },
                ct,
                timeoutSeconds: 30);

            _logger.LogDebug(
                "Alias inserted (if missing): {Alias} for ParsedId={ParsedId} | SourceCode={SourceCode}",
                alias,
                dictionaryEntryParsedId,
                sourceCode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to insert alias: {Alias} for ParsedId={ParsedId} | SourceCode={SourceCode}",
                alias,
                dictionaryEntryParsedId,
                sourceCode);
        }
    }
}