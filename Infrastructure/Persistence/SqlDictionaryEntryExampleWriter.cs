using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Common;
using Microsoft.Extensions.Logging;
using static DictionaryImporter.Common.Helper;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryExampleWriter(
        string connectionString,
        GenericSqlBatcher batcher,
        ISqlStoredProcedureExecutor sp,
        ILogger<SqlDictionaryEntryExampleWriter> logger)
        : IDictionaryEntryExampleWriter
    {
        private readonly ISqlStoredProcedureExecutor _sp = sp;

        public async Task WriteAsync(
            long dictionaryEntryParsedId,
            string exampleText,
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = SqlRepository.NormalizeSourceCode(sourceCode);

            if (dictionaryEntryParsedId <= 0)
                return;

            exampleText ??= string.Empty;
            exampleText = exampleText.Trim();

            if (string.IsNullOrWhiteSpace(exampleText))
                return;

            if (SqlRepository.IsPlaceholderExample(exampleText))
                return;

            bool hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(exampleText);
            long? nonEnglishTextId = null;
            string? exampleToStore = exampleText;

            if (hasNonEnglishText)
            {
                nonEnglishTextId = await SqlRepository.StoreNonEnglishTextAsync(
                    _sp,
                    originalText: exampleText,
                    sourceCode: sourceCode,
                    fieldType: "Example",
                    ct: ct,
                    timeoutSeconds: 30);

                exampleToStore = null;

                logger.LogDebug(
                    "Stored non-English example text for ParsedId={ParsedId}, NonEnglishTextId={TextId}",
                    dictionaryEntryParsedId, nonEnglishTextId);
            }
            else
            {
                exampleToStore = SqlRepository.NormalizeExampleForDedupeOrEmpty(exampleToStore ?? string.Empty);
                if (string.IsNullOrWhiteSpace(exampleToStore))
                    return;
            }

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
                ExampleText = exampleToStore,
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
    }
}
