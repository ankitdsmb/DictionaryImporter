using DictionaryImporter.Common;
using DictionaryImporter.Gateway.Rewriter;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntrySynonymWriter : IDictionaryEntrySynonymWriter, IDisposable
    {
        private const string NonEnglishSynonymPlaceholder = "[NON_ENGLISH]";

        private readonly string _connectionString;
        private readonly ILogger<SqlDictionaryEntrySynonymWriter> _logger;
        private readonly GenericSqlBatcher _batcher;
        private readonly ISqlStoredProcedureExecutor _sp;
        private readonly bool _ownsBatcher;

        public SqlDictionaryEntrySynonymWriter(
            string connectionString,
            ILogger<SqlDictionaryEntrySynonymWriter> logger,
            GenericSqlBatcher batcher,
            ISqlStoredProcedureExecutor sp)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _batcher = batcher ?? throw new ArgumentNullException(nameof(batcher));
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));
            _ownsBatcher = false;

            _logger.LogInformation("SqlDictionaryEntrySynonymWriter initialized with injected batcher");
        }

        public SqlDictionaryEntrySynonymWriter(
            string connectionString,
            ILogger<SqlDictionaryEntrySynonymWriter> logger,
            ISqlStoredProcedureExecutor sp)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));

            _batcher = CreateInternalBatcher();
            _ownsBatcher = true;

            _logger.LogInformation("SqlDictionaryEntrySynonymWriter created internal batcher");
        }

        private GenericSqlBatcher CreateInternalBatcher()
        {
            var nullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<GenericSqlBatcher>.Instance;
            return new GenericSqlBatcher(_connectionString, nullLogger);
        }

        public async Task WriteAsync(DictionaryEntrySynonym synonym, CancellationToken ct)
        {
            if (synonym == null)
                return;

            try
            {
                synonym.SourceCode = Helper.SqlRepository.NormalizeSourceCode(synonym.SourceCode);

                if (synonym.DictionaryEntryParsedId <= 0)
                    return;

                var rawSynonymText = (synonym.SynonymText ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(rawSynonymText))
                    return;

                var hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(rawSynonymText);
                long? nonEnglishTextId = null;

                string synonymToStore;

                if (hasNonEnglishText)
                {
                    nonEnglishTextId = await StoreNonEnglishTextDedupedAsync(
                        originalText: rawSynonymText,
                        sourceCode: synonym.SourceCode,
                        fieldType: "Synonym",
                        ct: ct);

                    synonymToStore = NonEnglishSynonymPlaceholder;
                }
                else
                {
                    var normalized = Helper.NormalizeSynonymText(rawSynonymText);
                    if (string.IsNullOrWhiteSpace(normalized))
                        return;

                    if (string.Equals(normalized, NonEnglishSynonymPlaceholder, StringComparison.Ordinal))
                        return;

                    synonymToStore = normalized;
                }

                if (string.IsNullOrWhiteSpace(synonymToStore))
                    return;

                if (synonymToStore.Length > 400)
                    synonymToStore = synonymToStore.Substring(0, 400);

                const string sql = """
IF NOT EXISTS (
    SELECT 1
    FROM dbo.DictionaryEntrySynonym WITH (UPDLOCK, HOLDLOCK)
    WHERE DictionaryEntryParsedId = @DictionaryEntryParsedId
      AND SourceCode = @SourceCode
      AND (
            (
                @HasNonEnglishText = 0
                AND SynonymText = @SynonymText
                AND NonEnglishTextId IS NULL
            )
            OR
            (
                @HasNonEnglishText = 1
                AND NonEnglishTextId = @NonEnglishTextId
            )
          )
)
BEGIN
    INSERT INTO dbo.DictionaryEntrySynonym
    (DictionaryEntryParsedId, SynonymText, SourceCode, CreatedUtc, HasNonEnglishText, NonEnglishTextId)
    VALUES
    (@DictionaryEntryParsedId, @SynonymText, @SourceCode, SYSUTCDATETIME(), @HasNonEnglishText, @NonEnglishTextId);
END
""";

                var parameters = new
                {
                    DictionaryEntryParsedId = synonym.DictionaryEntryParsedId,
                    SynonymText = synonymToStore,
                    SourceCode = synonym.SourceCode,
                    HasNonEnglishText = hasNonEnglishText,
                    NonEnglishTextId = nonEnglishTextId
                };

                await _batcher.QueueOperationAsync(
                    "INSERT_Synonym",
                    sql,
                    parameters,
                    CommandType.Text,
                    30,
                    ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "WriteAsync failed for synonym. ParsedId={ParsedId}, SourceCode={SourceCode}",
                    synonym.DictionaryEntryParsedId,
                    synonym.SourceCode);
            }
        }

        public async Task BulkWriteAsync(
            IEnumerable<DictionaryEntrySynonym> synonyms,
            CancellationToken ct)
        {
            if (synonyms == null)
                return;

            try
            {
                var synonymTable = CreateSynonymTable(synonyms);

                if (synonymTable.Rows.Count == 0)
                    return;

                const string sql = @"
INSERT INTO dbo.DictionaryEntrySynonym
(DictionaryEntryParsedId, SynonymText, SourceCode, CreatedUtc, HasNonEnglishText, NonEnglishTextId)
SELECT
    s.DictionaryEntryParsedId,
    s.SynonymText,
    ISNULL(s.SourceCode, 'UNKNOWN'),
    SYSUTCDATETIME(),
    0,
    NULL
FROM @Synonyms s
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.DictionaryEntrySynonym es WITH (UPDLOCK, HOLDLOCK)
    WHERE es.DictionaryEntryParsedId = s.DictionaryEntryParsedId
      AND es.SynonymText = s.SynonymText
      AND es.SourceCode = ISNULL(s.SourceCode, 'UNKNOWN')
      AND es.NonEnglishTextId IS NULL
);";

                var param = new
                {
                    Synonyms = synonymTable.AsTableValuedParameter("dbo.DictionaryEntrySynonymType")
                };

                await _batcher.ExecuteImmediateAsync(sql, param, CommandType.Text, 30, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BulkWriteAsync failed for synonyms bulk insert.");
            }
        }

        public async Task WriteSynonymsForParsedDefinition(
            long parsedDefinitionId,
            IEnumerable<string> synonyms,
            string sourceCode,
            CancellationToken ct)
        {
            if (parsedDefinitionId <= 0)
                return;

            sourceCode = Helper.SqlRepository.NormalizeSourceCode(sourceCode);

            var synonymList = synonyms?.ToList() ?? new List<string>();
            if (synonymList.Count == 0)
                return;

            var englishSynonyms = new List<string>();
            var nonEnglishSynonyms = new List<string>();

            foreach (var s in synonymList)
            {
                var raw = (s ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (Helper.LanguageDetector.ContainsNonEnglishText(raw))
                    nonEnglishSynonyms.Add(raw);
                else
                    englishSynonyms.Add(raw);
            }

            var uniqueEnglish = englishSynonyms
                .Select(Helper.NormalizeSynonymText)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Where(s => !string.Equals(s, NonEnglishSynonymPlaceholder, StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueEnglish.Count > 0)
            {
                _logger.LogDebug(
                    "Writing {Count} English synonyms for parsed definition {ParsedId} | SourceCode={SourceCode}",
                    uniqueEnglish.Count,
                    parsedDefinitionId,
                    sourceCode);

                var synonymObjects = uniqueEnglish.Select(s => new DictionaryEntrySynonym
                {
                    DictionaryEntryParsedId = parsedDefinitionId,
                    SynonymText = s,
                    SourceCode = sourceCode,
                    CreatedUtc = DateTime.UtcNow
                });

                await BulkWriteAsync(synonymObjects, ct);
            }

            var uniqueNonEnglish = nonEnglishSynonyms
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueNonEnglish.Count > 0)
            {
                _logger.LogDebug(
                    "Writing {Count} non-English synonyms for parsed definition {ParsedId} | SourceCode={SourceCode}",
                    uniqueNonEnglish.Count,
                    parsedDefinitionId,
                    sourceCode);

                foreach (var nonEng in uniqueNonEnglish)
                {
                    ct.ThrowIfCancellationRequested();

                    var obj = new DictionaryEntrySynonym
                    {
                        DictionaryEntryParsedId = parsedDefinitionId,
                        SynonymText = nonEng,
                        SourceCode = sourceCode,
                        CreatedUtc = DateTime.UtcNow
                    };

                    await WriteAsync(obj, ct);
                }
            }
        }

        private static DataTable CreateSynonymTable(IEnumerable<DictionaryEntrySynonym> synonyms)
        {
            var table = new DataTable();
            table.Columns.Add("DictionaryEntryParsedId", typeof(long));
            table.Columns.Add("SynonymText", typeof(string));
            table.Columns.Add("SourceCode", typeof(string));

            if (synonyms == null)
                return table;

            var uniqueSynonyms = synonyms
                .Where(s => s != null)
                .Select(s => new DictionaryEntrySynonym
                {
                    DictionaryEntryParsedId = s.DictionaryEntryParsedId,
                    SynonymText = Helper.NormalizeSynonymText(s.SynonymText),
                    SourceCode = Helper.SqlRepository.NormalizeSourceCode(s.SourceCode)
                })
                .Where(s => s.DictionaryEntryParsedId > 0)
                .Where(s => !string.IsNullOrWhiteSpace(s.SynonymText))
                .Where(s => !Helper.LanguageDetector.ContainsNonEnglishText(s.SynonymText!))
                .Where(s => !string.Equals(s.SynonymText, NonEnglishSynonymPlaceholder, StringComparison.Ordinal))
                .GroupBy(s => new { s.DictionaryEntryParsedId, s.SynonymText, s.SourceCode })
                .Select(g => g.First())
                .ToList();

            foreach (var synonym in uniqueSynonyms)
            {
                var text = synonym.SynonymText ?? string.Empty;
                if (text.Length > 400)
                    text = text.Substring(0, 400);

                table.Rows.Add(
                    synonym.DictionaryEntryParsedId,
                    text,
                    synonym.SourceCode);
            }

            return table;
        }

        private async Task<long?> StoreNonEnglishTextDedupedAsync(
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct)
        {
            try
            {
                return await Helper.SqlRepository.StoreNonEnglishTextAsync(
                    _sp,
                    originalText: originalText,
                    sourceCode: sourceCode,
                    fieldType: fieldType,
                    ct: ct,
                    timeoutSeconds: 60);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "StoreNonEnglishTextDedupedAsync failed. SourceCode={SourceCode}, FieldType={FieldType}",
                    sourceCode,
                    fieldType);

                return null;
            }
        }

        public void Dispose()
        {
            if (_ownsBatcher)
                _batcher?.Dispose();
        }
    }
}
