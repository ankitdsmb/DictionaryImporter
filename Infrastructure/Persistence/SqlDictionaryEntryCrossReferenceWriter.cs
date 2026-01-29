using System;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryCrossReferenceWriter(
    ISqlStoredProcedureExecutor sp,
    ILogger<SqlDictionaryEntryCrossReferenceWriter> logger)
    : IDictionaryEntryCrossReferenceWriter
{
    private readonly ISqlStoredProcedureExecutor _sp = sp;
    private readonly ILogger<SqlDictionaryEntryCrossReferenceWriter> _logger = logger;

    public async Task WriteAsync(
        long parsedDefinitionId,
        CrossReference crossRef,
        string sourceCode,
        CancellationToken ct)
    {
        if (crossRef == null)
            throw new ArgumentNullException(nameof(crossRef));

        if (parsedDefinitionId <= 0)
            return;

        sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

        var target = Helper.SqlRepository.NormalizeCrossReferenceTargetOrEmpty(crossRef.TargetWord);
        if (string.IsNullOrWhiteSpace(target))
            return;

        var type = Helper.SqlRepository.NormalizeCrossReferenceTypeOrEmpty(crossRef.ReferenceType);
        if (string.IsNullOrWhiteSpace(type))
            type = "related";

        try
        {
            var rows = await _sp.ExecuteAsync(
                "sp_DictionaryEntryCrossReference_InsertIfMissing",
                new
                {
                    ParsedId = parsedDefinitionId,
                    Target = target,
                    Type = type,
                    SourceCode = sourceCode
                },
                ct,
                timeoutSeconds: 30);

            if (rows > 0)
            {
                _logger.LogDebug(
                    "CrossReference inserted | ParsedId={ParsedId} | Type={Type} | Target={Target} | SourceCode={SourceCode}",
                    parsedDefinitionId,
                    type,
                    target,
                    sourceCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to insert CrossReference | ParsedId={ParsedId} | Type={Type} | Target={Target} | SourceCode={SourceCode}",
                parsedDefinitionId,
                type,
                target,
                sourceCode);
        }
    }
}