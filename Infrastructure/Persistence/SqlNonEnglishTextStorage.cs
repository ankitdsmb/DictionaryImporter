using LanguageDetector = DictionaryImporter.Core.Text.LanguageDetector;

namespace DictionaryImporter.Infrastructure.Persistence;

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
        string fieldType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(originalText))
            return null;

        sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();
        fieldType = string.IsNullOrWhiteSpace(fieldType) ? "Unknown" : fieldType.Trim();

        // Only store if it is actually non-English
        if (!LanguageDetector.ContainsNonEnglishText(originalText))
            return null;

        // ✅ DB requires FieldType NOT NULL (per your other writer logic)
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
            FieldType = fieldType
        };

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var textId = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));

            _cache[textId] = originalText;

            _logger.LogDebug(
                "Stored non-English text: ID={TextId}, Language={Language}, Field={FieldType}, Length={Length}",
                textId, languageCode, fieldType, originalText.Length);

            return textId;
        }
        catch (Exception ex)
        {
            // ✅ Never crash importer
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

        const string sql = """
                           SELECT OriginalText
                           FROM dbo.DictionaryNonEnglishText
                           WHERE NonEnglishTextId = @NonEnglishTextId;
                           """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var text = await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(
                    sql,
                    new { NonEnglishTextId = nonEnglishTextId },
                    cancellationToken: ct));

            if (!string.IsNullOrWhiteSpace(text))
                _cache[nonEnglishTextId] = text;

            return text;
        }
        catch (Exception ex)
        {
            // ✅ Never crash importer
            _logger.LogDebug(
                ex,
                "Failed to load non-English text for NonEnglishTextId={NonEnglishTextId}",
                nonEnglishTextId);

            return null;
        }
    }

    // NEW METHOD (added)
    public async Task<IReadOnlyDictionary<long, string>> GetNonEnglishTextBatchAsync(
        IEnumerable<long> nonEnglishTextIds,
        CancellationToken ct)
    {
        var ids = nonEnglishTextIds?
            .Where(x => x > 0)
            .Distinct()
            .ToList() ?? new List<long>();

        if (ids.Count == 0)
            return new Dictionary<long, string>();

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

        const string sql = """
                           SELECT NonEnglishTextId, OriginalText
                           FROM dbo.DictionaryNonEnglishText
                           WHERE NonEnglishTextId IN @Ids;
                           """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var rows = await connection.QueryAsync<(long NonEnglishTextId, string OriginalText)>(
                new CommandDefinition(
                    sql,
                    new { Ids = missingIds },
                    cancellationToken: ct));

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.OriginalText))
                    continue;

                result[row.NonEnglishTextId] = row.OriginalText;
                _cache[row.NonEnglishTextId] = row.OriginalText;
            }

            return result;
        }
        catch (Exception ex)
        {
            // ✅ Never crash importer
            _logger.LogDebug(ex, "Failed to batch load non-English texts");

            return result;
        }
    }
}