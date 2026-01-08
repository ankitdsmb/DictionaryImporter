using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryAliasWriter
    {
        private readonly string _connectionString;

        public SqlDictionaryAliasWriter(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task WriteAsync(
            long dictionaryEntryParsedId,
            string alias,
            CancellationToken ct)
        {
            const string sql =
                """
                INSERT INTO dbo.DictionaryEntryAlias
                (
                    DictionaryEntryParsedId,
                    AliasText,
                    CreatedUtc
                )
                VALUES
                (
                    @DictionaryEntryParsedId,
                    @AliasText,
                    SYSUTCDATETIME()
                )
                """;

            await using var conn =
                new SqlConnection(_connectionString);

            await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        DictionaryEntryParsedId = dictionaryEntryParsedId,
                        AliasText = alias
                    },
                    cancellationToken: ct));
        }
    }
}
