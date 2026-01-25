using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Abstractions.Persistence;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlNonEnglishTextStorage : INonEnglishTextStorage
    {
        private readonly ISqlStoredProcedureExecutor _sp;
        private readonly ILogger<SqlNonEnglishTextStorage> _logger;
        private readonly ConcurrentDictionary<long, string> _cache = new();

        public SqlNonEnglishTextStorage(
            ISqlStoredProcedureExecutor sp,
            ILogger<SqlNonEnglishTextStorage> logger)
        {
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<long?> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct)
        {
            originalText = (originalText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(originalText))
                return null;

            sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);
            fieldType = Helper.SqlRepository.NormalizeString(fieldType, "Unknown");

            if (!Helper.LanguageDetector.ContainsNonEnglishText(originalText))
                return null;

            try
            {
                var textId = await Helper.SqlRepository.StoreNonEnglishTextAsync(
                    _sp,
                    originalText: originalText,
                    sourceCode: sourceCode,
                    fieldType: fieldType,
                    ct: ct,
                    timeoutSeconds: 30);

                if (textId.HasValue && textId.Value > 0)
                {
                    _cache[textId.Value] = originalText;

                    _logger.LogDebug(
                        "Stored non-English text: ID={TextId}, Field={FieldType}, Length={Length}",
                        textId.Value,
                        fieldType,
                        originalText.Length);

                    return textId.Value;
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to store non-English text. Source={SourceCode}, Field={FieldType}, Length={Length}",
                    sourceCode,
                    fieldType,
                    originalText.Length);

                return null;
            }
        }

        public async Task<string?> GetNonEnglishTextAsync(
            long nonEnglishTextId,
            CancellationToken ct)
        {
            if (nonEnglishTextId <= 0)
                return null;

            if (_cache.TryGetValue(nonEnglishTextId, out var cachedText))
                return cachedText;

            try
            {
                var text = await _sp.ExecuteScalarAsync<string?>(
                    "sp_DictionaryNonEnglishText_GetById",
                    new { NonEnglishTextId = nonEnglishTextId },
                    ct,
                    timeoutSeconds: 30);

                if (!string.IsNullOrWhiteSpace(text))
                    _cache[nonEnglishTextId] = text.Trim();

                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Failed to load non-English text for NonEnglishTextId={NonEnglishTextId}",
                    nonEnglishTextId);

                return null;
            }
        }

        public async Task<IReadOnlyDictionary<long, string>> GetNonEnglishTextBatchAsync(
            IEnumerable<long> nonEnglishTextIds,
            CancellationToken ct)
        {
            var ids = Helper.SqlRepository.NormalizeDistinctIds(nonEnglishTextIds);
            if (ids.Length == 0)
                return new Dictionary<long, string>();

            var result = new Dictionary<long, string>(ids.Length);
            var missingIds = new List<long>();

            foreach (var id in ids)
            {
                if (_cache.TryGetValue(id, out var cachedText))
                    result[id] = cachedText;
                else
                    missingIds.Add(id);
            }

            if (missingIds.Count == 0)
                return result;

            try
            {
                var tvp = Helper.SqlRepository.ToBigIntIdListTvp(missingIds);

                var rows = await _sp.QueryAsync<NonEnglishTextRow>(
                    "sp_DictionaryNonEnglishText_GetBatch",
                    new { Ids = tvp },
                    ct,
                    timeoutSeconds: 60);

                foreach (var row in rows)
                {
                    if (row.NonEnglishTextId <= 0)
                        continue;

                    var text = (row.OriginalText ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    result[row.NonEnglishTextId] = text;
                    _cache[row.NonEnglishTextId] = text;
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to batch load non-English texts");
                return result;
            }
        }

        private sealed class NonEnglishTextRow
        {
            public long NonEnglishTextId { get; set; }
            public string? OriginalText { get; set; }
        }
    }
}
