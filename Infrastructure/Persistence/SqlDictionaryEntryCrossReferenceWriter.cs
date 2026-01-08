using Dapper;
using DictionaryImporter.Domain.Models;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryCrossReferenceWriter
    {
        private readonly string _cs;

        public SqlDictionaryEntryCrossReferenceWriter(string cs)
        {
            _cs = cs;
        }

        public async Task WriteAsync(
            long parsedDefinitionId,
            CrossReference crossRef,
            CancellationToken ct)
        {
            await using var conn = new SqlConnection(_cs);

            await conn.ExecuteAsync(
                """
                INSERT INTO dbo.DictionaryEntryCrossReference
                (SourceParsedId, TargetWord, ReferenceType)
                VALUES (@ParsedId, @Target, @Type)
                """,
                new
                {
                    ParsedId = parsedDefinitionId,
                    Target = crossRef.TargetWord,
                    Type = crossRef.ReferenceType
                });
        }
    }
}