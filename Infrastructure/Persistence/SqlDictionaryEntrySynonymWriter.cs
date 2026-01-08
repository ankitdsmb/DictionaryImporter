using Dapper;
using DictionaryImporter.Domain.Models;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntrySynonymWriter
    {
        private readonly string _connectionString;

        public SqlDictionaryEntrySynonymWriter(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task WriteAsync(
            DictionaryEntrySynonym synonym,
            CancellationToken ct)
        {
            const string sql =
                """
                INSERT INTO dbo.DictionaryEntrySynonym
                (
                    DictionaryEntryParsedId,
                    SynonymText,
                    CreatedUtc
                )
                VALUES
                (
                    @DictionaryEntryParsedId,
                    @SynonymText,
                    @CreatedUtc
                );
                """;

            await using var conn =
                new SqlConnection(_connectionString);

            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    synonym,
                    cancellationToken: ct));
        }
    }
}
