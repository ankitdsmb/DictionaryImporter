// Add this file as SqlDictionaryEntryExampleWriter.cs
using Dapper;
using DictionaryImporter.Core.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryExampleWriter : IDictionaryEntryExampleWriter
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlDictionaryEntryExampleWriter> _logger;

        public SqlDictionaryEntryExampleWriter(
            string connectionString,
            ILogger<SqlDictionaryEntryExampleWriter> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task WriteAsync(
            long parsedDefinitionId,
            string exampleText,
            string sourceCode,
            CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.DictionaryEntryExample
                (
                    DictionaryEntryParsedId,
                    ExampleText,
                    Source,
                    CreatedUtc
                )
                VALUES
                (
                    @ParsedId,
                    @ExampleText,
                    @SourceCode,
                    SYSUTCDATETIME()
                );
                """;

            await using var conn = new SqlConnection(_connectionString);

            var rows = await conn.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new
                    {
                        ParsedId = parsedDefinitionId,
                        ExampleText = exampleText,
                        SourceCode = sourceCode
                    },
                    cancellationToken: ct));

            if (rows > 0)
            {
                _logger.LogDebug(
                    "Example inserted | ParsedId={ParsedId} | Source={Source}",
                    parsedDefinitionId,
                    sourceCode);
            }
        }
    }
}