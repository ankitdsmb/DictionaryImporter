using System;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryEtymologyWriter(
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlDictionaryEntryEtymologyWriter> logger)
    : IEntryEtymologyWriter
{
    private readonly ISqlStoredProcedureExecutor _sp = sp;
    private readonly ILogger<SqlDictionaryEntryEtymologyWriter> _logger = logger;

    public async Task WriteAsync(DictionaryEntryEtymology etymology, CancellationToken ct)
    {
        if (etymology == null)
            throw new ArgumentNullException(nameof(etymology));

        if (etymology.DictionaryEntryId <= 0)
            return;

        var sourceCode = Helper.SqlRepository.NormalizeSourceCode(etymology.SourceCode);

        var etymologyText = Helper.SqlRepository.NormalizeEtymologyTextOrEmpty(etymology.EtymologyText);
        if (string.IsNullOrWhiteSpace(etymologyText))
            return;

        var languageCode = Helper.SqlRepository.NormalizeNullableString(etymology.LanguageCode, maxLen: 32);

        try
        {
            await _sp.ExecuteAsync(
                "sp_DictionaryEntryEtymology_InsertIfMissing",
                new
                {
                    DictionaryEntryId = etymology.DictionaryEntryId,
                    EtymologyText = etymologyText,
                    LanguageCode = languageCode,
                    SourceCode = sourceCode
                },
                ct,
                timeoutSeconds: 30);

            _logger.LogDebug(
                "Wrote etymology (if missing) for DictionaryEntryId={EntryId} | SourceCode={SourceCode}",
                etymology.DictionaryEntryId,
                sourceCode);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to write etymology for DictionaryEntryId={EntryId} | SourceCode={SourceCode}",
                etymology.DictionaryEntryId,
                sourceCode);
        }
    }
}