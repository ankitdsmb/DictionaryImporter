// File: Infrastructure/Persistence/SqlParsedDefinitionWriter.cs
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Infrastructure.Persistence.Batched;
using LanguageDetector = DictionaryImporter.Core.Text.LanguageDetector;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlParsedDefinitionWriter
    {
        private readonly string _connectionString;
        private readonly GenericSqlBatcher _batcher;
        private readonly ILogger<SqlParsedDefinitionWriter> _logger;

        public SqlParsedDefinitionWriter(string connectionString, GenericSqlBatcher batcher, ILogger<SqlParsedDefinitionWriter> logger)
        {
            _connectionString = connectionString;
            _batcher = batcher;
            _logger = logger;
        }

        public async Task<long> WriteAsync(
            long dictionaryEntryId,
            ParsedDefinition parsed,
            string sourceCode,
            CancellationToken ct)
        {
            // Check for non-English content
            bool hasNonEnglishText = LanguageDetector.ContainsNonEnglishText(parsed.Definition ?? "");
            long? nonEnglishTextId = null;
            string definitionToStore = parsed.Definition ?? "";

            if (hasNonEnglishText)
            {
                _logger.LogDebug(
                    "Non-English text detected for DictionaryEntryId={DictionaryEntryId}, Source={Source}",
                    dictionaryEntryId, sourceCode);

                // Store original in non-English table
                nonEnglishTextId = await StoreNonEnglishTextAsync(
                    definitionToStore,
                    sourceCode,
                    ct);

                // Replace with placeholder for main table
                definitionToStore = "[NON_ENGLISH]";

                _logger.LogInformation(
                    "Stored non-English text for DictionaryEntryId={DictionaryEntryId}, NonEnglishTextId={TextId}, Language={Language}",
                    dictionaryEntryId, nonEnglishTextId,
                    LanguageDetector.DetectLanguageCode(parsed.Definition ?? ""));
            }

            const string sql = """
                INSERT INTO dbo.DictionaryEntryParsed (
                    DictionaryEntryId, ParentParsedId, MeaningTitle,
                    Definition, RawFragment, SenseNumber,
                    Domain, UsageLabel, Alias,
                    HasNonEnglishText, NonEnglishTextId, SourceCode,
                    CreatedUtc
                ) VALUES (
                    @DictionaryEntryId, @ParentParsedId, @MeaningTitle,
                    @Definition, @RawFragment, @SenseNumber,
                    @Domain, @UsageLabel, @Alias,
                    @HasNonEnglishText, @NonEnglishTextId, @SourceCode,
                    SYSUTCDATETIME()
                );
                SELECT SCOPE_IDENTITY();
                """;

            var parameters = new
            {
                DictionaryEntryId = dictionaryEntryId,
                ParentParsedId = (object?)parsed.ParentParsedId ?? DBNull.Value,
                MeaningTitle = parsed.MeaningTitle ?? "",
                Definition = definitionToStore,
                RawFragment = parsed.RawFragment ?? "",
                SenseNumber = parsed.SenseNumber,
                Domain = (object?)parsed.Domain ?? DBNull.Value,
                UsageLabel = (object?)parsed.UsageLabel ?? DBNull.Value,
                Alias = (object?)parsed.Alias ?? DBNull.Value,
                HasNonEnglishText = hasNonEnglishText,
                NonEnglishTextId = (object?)nonEnglishTextId ?? DBNull.Value,
                SourceCode = sourceCode
            };

            // Use batched execution for performance
            await _batcher.QueueOperationAsync(
                "INSERT_ParsedDefinition",
                sql,
                parameters,
                CommandType.Text,
                30);

            // Return optimistic result (batched operation)
            return 0;
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

        // Batch version for better performance
        public async Task WriteBatchAsync(
            IEnumerable<(long DictionaryEntryId, ParsedDefinition Parsed, string SourceCode)> entries,
            CancellationToken ct)
        {
            var batchEntries = new List<object>();

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                bool hasNonEnglishText = LanguageDetector.ContainsNonEnglishText(entry.Parsed.Definition ?? "");
                string definitionToStore = entry.Parsed.Definition ?? "";
                long? nonEnglishTextId = null;

                if (hasNonEnglishText)
                {
                    // For batch processing, we'll store non-English text separately
                    // and use a placeholder in the batch insert
                    definitionToStore = "[NON_ENGLISH]";
                    // Note: In batch mode, we'd need to handle non-English text storage differently
                    // This is a simplification for the initial implementation
                }

                batchEntries.Add(new
                {
                    DictionaryEntryId = entry.DictionaryEntryId,
                    ParentParsedId = (object?)entry.Parsed.ParentParsedId ?? DBNull.Value,
                    MeaningTitle = entry.Parsed.MeaningTitle ?? "",
                    Definition = definitionToStore,
                    RawFragment = entry.Parsed.RawFragment ?? "",
                    SenseNumber = entry.Parsed.SenseNumber,
                    Domain = (object?)entry.Parsed.Domain ?? DBNull.Value,
                    UsageLabel = (object?)entry.Parsed.UsageLabel ?? DBNull.Value,
                    Alias = (object?)entry.Parsed.Alias ?? DBNull.Value,
                    HasNonEnglishText = hasNonEnglishText,
                    NonEnglishTextId = (object?)nonEnglishTextId ?? DBNull.Value,
                    SourceCode = entry.SourceCode
                });
            }

            if (batchEntries.Count > 0)
            {
                const string batchSql = """
                    INSERT INTO dbo.DictionaryEntryParsed (
                        DictionaryEntryId, ParentParsedId, MeaningTitle,
                        Definition, RawFragment, SenseNumber,
                        Domain, UsageLabel, Alias,
                        HasNonEnglishText, NonEnglishTextId, SourceCode,
                        CreatedUtc
                    ) VALUES (
                        @DictionaryEntryId, @ParentParsedId, @MeaningTitle,
                        @Definition, @RawFragment, @SenseNumber,
                        @Domain, @UsageLabel, @Alias,
                        @HasNonEnglishText, @NonEnglishTextId, @SourceCode,
                        SYSUTCDATETIME()
                    );
                    """;

                await _batcher.QueueOperationAsync(
                    "BATCH_INSERT_ParsedDefinition",
                    batchSql,
                    batchEntries,
                    CommandType.Text,
                    30);

                _logger.LogInformation(
                    "Queued batch of {Count} parsed definitions for insertion",
                    batchEntries.Count);
            }
        }
    }
}