using System;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryVariantWriter(
        ISqlStoredProcedureExecutor sp,
        ILogger<SqlDictionaryEntryVariantWriter> logger)
        : IDictionaryEntryVariantWriter
    {
        private readonly ISqlStoredProcedureExecutor _sp = sp;
        private readonly ILogger<SqlDictionaryEntryVariantWriter> _logger = logger;

        public async Task WriteAsync(
            long entryId,
            string variant,
            string type,
            string sourceCode,
            CancellationToken ct)
        {
            if (entryId <= 0)
                return;

            sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

            variant = (variant ?? string.Empty).Trim();
            type = (type ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(type))
                return;

            variant = Helper.SqlRepository.Truncate(variant, 200);
            type = Helper.SqlRepository.Truncate(type, 50);

            try
            {
                await _sp.ExecuteAsync(
                    "sp_DictionaryEntryVariant_InsertIfMissing",
                    new
                    {
                        EntryId = entryId,
                        Variant = variant,
                        Type = type,
                        SourceCode = sourceCode
                    },
                    ct,
                    timeoutSeconds: 30);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to insert variant | EntryId={EntryId} | Type={Type} | Variant={Variant} | SourceCode={SourceCode}",
                    entryId,
                    type,
                    variant,
                    sourceCode);
            }
        }
    }
}
