// File: Infrastructure/Persistence/INonEnglishTextStorage.cs
using System.Collections.Concurrent;
using DictionaryImporter.Infrastructure.Persistence.Batched;
using LanguageDetector = DictionaryImporter.Core.Text.LanguageDetector;

namespace DictionaryImporter.Infrastructure.Persistence
{
    // In INonEnglishTextStorage.cs - fix the interface method
    public interface INonEnglishTextStorage
    {
        Task<long?> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            string fieldType,  // Add this parameter
            CancellationToken ct);

        Task<string?> GetNonEnglishTextAsync(long nonEnglishTextId, CancellationToken ct);
    }

    public class SqlNonEnglishTextStorage : INonEnglishTextStorage
    {
        private readonly string _connectionString;
        private readonly GenericSqlBatcher _batcher;
        private readonly ILogger<SqlNonEnglishTextStorage> _logger;
        private readonly ConcurrentDictionary<long, string> _cache = new();

        public SqlNonEnglishTextStorage(
            string connectionString,
            GenericSqlBatcher batcher,
            ILogger<SqlNonEnglishTextStorage> logger)
        {
            _connectionString = connectionString;
            _batcher = batcher;
            _logger = logger;
        }

        public async Task<long?> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(originalText))
                return null;

            // Check if it's actually non-English
            if (!LanguageDetector.ContainsNonEnglishText(originalText))
                return null;

            const string sql = """
                INSERT INTO dbo.DictionaryNonEnglishText (
                    OriginalText, DetectedLanguage, CharacterCount,
                    SourceCode, CreatedUtc
                ) OUTPUT INSERTED.NonEnglishTextId
                VALUES (
                    @OriginalText, @DetectedLanguage, @CharacterCount,
                    @SourceCode, SYSUTCDATETIME()
                );
                """;

            var languageCode = LanguageDetector.DetectLanguageCode(originalText);

            var parameters = new
            {
                OriginalText = originalText,
                DetectedLanguage = languageCode ?? (object)DBNull.Value,
                CharacterCount = originalText.Length,
                SourceCode = sourceCode
            };

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var textId = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));

            // Cache the result
            _cache[textId] = originalText;

            _logger.LogDebug(
                "Stored non-English text: ID={TextId}, Language={Language}, Length={Length}",
                textId, languageCode, originalText.Length);

            return textId;
        }

        public async Task<string?> GetNonEnglishTextAsync(
            long nonEnglishTextId,
            CancellationToken ct)
        {
            // Check cache first
            if (_cache.TryGetValue(nonEnglishTextId, out var cachedText))
                return cachedText;

            const string sql = """
                SELECT OriginalText
                FROM dbo.DictionaryNonEnglishText
                WHERE NonEnglishTextId = @NonEnglishTextId;
                """;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var text = await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(sql, new { NonEnglishTextId = nonEnglishTextId }, cancellationToken: ct));

            if (text != null)
                _cache[nonEnglishTextId] = text;

            return text;
        }

        public async Task<IReadOnlyDictionary<long, string>> GetNonEnglishTextBatchAsync(
            IEnumerable<long> nonEnglishTextIds,
            CancellationToken ct)
        {
            var ids = nonEnglishTextIds.ToList();
            if (ids.Count == 0)
                return new Dictionary<long, string>();

            // Check cache for what we have
            var result = new Dictionary<long, string>();
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

            // Fetch missing ones from database
            const string sql = """
                SELECT NonEnglishTextId, OriginalText
                FROM dbo.DictionaryNonEnglishText
                WHERE NonEnglishTextId IN @Ids;
                """;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var rows = await connection.QueryAsync<(long Id, string Text)>(
                new CommandDefinition(sql, new { Ids = missingIds }, cancellationToken: ct));

            foreach (var row in rows)
            {
                result[row.Id] = row.Text;
                _cache[row.Id] = row.Text;
            }

            return result;
        }

        public async Task<long?> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(originalText))
                return null;

            // Check if it's actually non-English
            if (!LanguageDetector.ContainsNonEnglishText(originalText))
                return null;

            // FIXED SQL SYNTAX - Added missing FieldType column
            const string sql = """
                               INSERT INTO dbo.DictionaryNonEnglishText (
                                   OriginalText, 
                                   DetectedLanguage, 
                                   CharacterCount,
                                   SourceCode, 
                                   FieldType, 
                                   CreatedUtc
                               ) OUTPUT INSERTED.NonEnglishTextId
                               VALUES (
                                   @OriginalText, 
                                   @DetectedLanguage, 
                                   @CharacterCount,
                                   @SourceCode, 
                                   @FieldType, 
                                   SYSUTCDATETIME()
                               );
                               """;

            var languageCode = LanguageDetector.DetectLanguageCode(originalText);

            var parameters = new
            {
                OriginalText = originalText,
                DetectedLanguage = languageCode ?? (object)DBNull.Value,
                CharacterCount = originalText.Length,
                SourceCode = sourceCode,
                FieldType = fieldType  // ADD THIS
            };

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var textId = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));

            // Cache the result
            _cache[textId] = originalText;

            _logger.LogDebug(
                "Stored non-English text: ID={TextId}, Language={Language}, Field={FieldType}, Length={Length}",
                textId, languageCode, fieldType, originalText.Length);

            return textId;
        }
    }
}