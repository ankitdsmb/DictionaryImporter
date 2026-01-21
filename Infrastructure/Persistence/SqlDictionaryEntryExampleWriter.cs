// File: Infrastructure/Persistence/SqlDictionaryEntryExampleWriter.cs
using Dapper;
using DictionaryImporter.Core.Persistence; // Add this using
using DictionaryImporter.Core.Text;
using DictionaryImporter.Infrastructure.Persistence.Batched;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using LanguageDetector = DictionaryImporter.Core.Text.LanguageDetector;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryExampleWriter : IDictionaryEntryExampleWriter // Implement interface
    {
        private readonly string _connectionString;
        private readonly GenericSqlBatcher _batcher;
        private readonly ILogger<SqlDictionaryEntryExampleWriter> _logger;

        public SqlDictionaryEntryExampleWriter(
            string connectionString,
            GenericSqlBatcher batcher,
            ILogger<SqlDictionaryEntryExampleWriter> logger)
        {
            _connectionString = connectionString;
            _batcher = batcher;
            _logger = logger;
        }

        public async Task WriteAsync(
            long dictionaryEntryParsedId,
            string exampleText,
            string sourceCode,
            CancellationToken ct)
        {
            // Check for non-English content
            bool hasNonEnglishText = LanguageDetector.ContainsNonEnglishText(exampleText);
            long? nonEnglishTextId = null;
            string exampleToStore = exampleText;

            if (hasNonEnglishText)
            {
                // Store original in non-English table
                nonEnglishTextId = await StoreNonEnglishTextAsync(
                    exampleText,
                    sourceCode,
                    ct);

                // Replace with placeholder for main table
                exampleToStore = "[NON_ENGLISH]";

                _logger.LogDebug(
                    "Stored non-English example text for ParsedId={ParsedId}, NonEnglishTextId={TextId}",
                    dictionaryEntryParsedId, nonEnglishTextId);
            }

            const string sql = """
                INSERT INTO dbo.DictionaryEntryExample (
                    DictionaryEntryParsedId, ExampleText,
                    SourceCode, HasNonEnglishText, NonEnglishTextId,
                    CreatedUtc
                ) VALUES (
                    @DictionaryEntryParsedId, @ExampleText,
                    @SourceCode, @HasNonEnglishText, @NonEnglishTextId,
                    SYSUTCDATETIME()
                );
                """;

            var parameters = new
            {
                DictionaryEntryParsedId = dictionaryEntryParsedId,
                ExampleText = exampleToStore,
                SourceCode = sourceCode,
                HasNonEnglishText = hasNonEnglishText,
                NonEnglishTextId = (object?)nonEnglishTextId ?? DBNull.Value
            };

            await _batcher.QueueOperationAsync(
                "INSERT_Example",
                sql,
                parameters,
                CommandType.Text,
                30);
        }

        private async Task<long> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            CancellationToken ct)
        {
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

            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
    }
}