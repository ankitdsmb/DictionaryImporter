using DictionaryImporter.Common;
using static DictionaryImporter.Common.Helper;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryExampleWriter(
        string connectionString,
        GenericSqlBatcher batcher,
        ILogger<SqlDictionaryEntryExampleWriter> logger)
        : IDictionaryEntryExampleWriter
    {
        public async Task WriteAsync(
            long dictionaryEntryParsedId,
            string exampleText,
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode;

            if (dictionaryEntryParsedId <= 0)
                return;

            exampleText ??= string.Empty;
            exampleText = exampleText.Trim();

            if (string.IsNullOrWhiteSpace(exampleText))
                return;

            // Do not store placeholders
            if (IsPlaceholderExample(exampleText))
                return;

            bool hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(exampleText);
            long? nonEnglishTextId = null;
            string? exampleToStore = exampleText;

            if (hasNonEnglishText)
            {
                nonEnglishTextId = await StoreNonEnglishTextAsync(
                    originalText: exampleText,
                    sourceCode: sourceCode,
                    fieldType: "Example",
                    ct);

                // Non-English example stored via NonEnglishTextId, do not store ExampleText
                exampleToStore = null;

                logger.LogDebug(
                    "Stored non-English example text for ParsedId={ParsedId}, NonEnglishTextId={TextId}",
                    dictionaryEntryParsedId, nonEnglishTextId);
            }
            else
            {
                exampleToStore = NormalizeExampleForDedupe(exampleToStore ?? string.Empty);
                if (string.IsNullOrWhiteSpace(exampleToStore))
                    return;
            }

            // ✅ IMPORTANT:
            // ExampleHash is COMPUTED => never insert/update it.

            // FIX:
            // Deduplicate at DictionaryEntryId (word-level), not only ParsedId-level.
            // This prevents the same example being inserted multiple times for multiple senses.
            const string sql = """
                DECLARE @DictionaryEntryId BIGINT;

                SELECT @DictionaryEntryId = p.DictionaryEntryId
                FROM dbo.DictionaryEntryParsed p WITH (NOLOCK)
                WHERE p.DictionaryEntryParsedId = @DictionaryEntryParsedId;

                IF @DictionaryEntryId IS NULL
                    RETURN;

                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.DictionaryEntryExample e WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.DictionaryEntryParsed p2 WITH (NOLOCK)
                        ON p2.DictionaryEntryParsedId = e.DictionaryEntryParsedId
                    WHERE p2.DictionaryEntryId = @DictionaryEntryId
                      AND e.SourceCode = @SourceCode
                      AND (
                            (
                                @HasNonEnglishText = 0
                                AND e.NonEnglishTextId IS NULL
                                AND e.ExampleText = @ExampleText
                            )
                            OR
                            (
                                @HasNonEnglishText = 1
                                AND e.NonEnglishTextId = @NonEnglishTextId
                            )
                          )
                )
                BEGIN
                    INSERT INTO dbo.DictionaryEntryExample (
                        DictionaryEntryParsedId,
                        ExampleText,
                        SourceCode,
                        CreatedUtc,
                        HasNonEnglishText,
                        NonEnglishTextId
                    ) VALUES (
                        @DictionaryEntryParsedId,
                        @ExampleText,
                        @SourceCode,
                        SYSUTCDATETIME(),
                        @HasNonEnglishText,
                        @NonEnglishTextId
                    );
                END
                """;

            var parameters = new
            {
                DictionaryEntryParsedId = dictionaryEntryParsedId,
                ExampleText = exampleToStore, // null for non-English
                SourceCode = sourceCode,
                HasNonEnglishText = hasNonEnglishText,
                NonEnglishTextId = nonEnglishTextId
            };

            await batcher.QueueOperationAsync(
                "INSERT_Example",
                sql,
                parameters,
                CommandType.Text,
                30,
                ct);
        }

        private static bool IsPlaceholderExample(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var t = text.Trim();

            return t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase)
                || t.Equals("[BILINGUAL_EXAMPLE]", StringComparison.OrdinalIgnoreCase)
                || t.Equals("NON_ENGLISH", StringComparison.OrdinalIgnoreCase)
                || t.Equals("BILINGUAL_EXAMPLE", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<long> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct)
        {
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
                DetectedLanguage = languageCode,
                CharacterCount = originalText.Length,
                SourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode,
                FieldType = fieldType
            };

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
        }

        private static string NormalizeExampleForDedupe(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            var t = example.Trim();

            t = Regex.Replace(t, @"\s+", " ").Trim();

            if (t.Length > 800)
                t = t.Substring(0, 800).Trim();

            return t;
        }
    }
}
