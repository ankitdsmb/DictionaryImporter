using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryVariantWriter(
        string cs,
        ILogger<SqlDictionaryEntryVariantWriter> logger) : IDictionaryEntryVariantWriter
    {
        public async Task WriteAsync(
            long entryId,
            string variant,
            string type,
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode;
            variant = variant?.Trim() ?? string.Empty;
            type = type?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(variant) || string.IsNullOrWhiteSpace(type))
                return;

            const string sql = """
                               INSERT INTO dbo.DictionaryEntryVariant
                               (DictionaryEntryId, VariantText, VariantType, SourceCode)
                               SELECT @EntryId, @Variant, @Type, @SourceCode
                               WHERE NOT EXISTS
                               (
                                   SELECT 1
                                   FROM dbo.DictionaryEntryVariant
                                   WHERE DictionaryEntryId = @EntryId
                                     AND VariantText = @Variant
                                     AND VariantType = @Type
                                     AND SourceCode = @SourceCode
                               );
                               """;

            await using var conn = new SqlConnection(cs);

            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        EntryId = entryId,
                        Variant = variant,
                        Type = type,
                        SourceCode = sourceCode
                    },
                    cancellationToken: ct));
        }
    }
}
