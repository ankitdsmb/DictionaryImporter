using Dapper;
using DictionaryImporter.Domain.Models;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlParsedDefinitionWriter
    {
        private readonly string _connectionString;

        public SqlParsedDefinitionWriter(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<long> WriteAsync(
            long dictionaryEntryId,
            ParsedDefinition parsed,
            long? parentParsedId,
            CancellationToken ct)
        {
            const string sql = @"
INSERT INTO dbo.DictionaryEntryParsed
(
    DictionaryEntryId,
    ParentParsedId,
    MeaningTitle,
    SenseNumber,
    DomainCode,
    UsageLabel,
    Definition,
    RawFragment,
    CreatedUtc
)
OUTPUT INSERTED.DictionaryEntryParsedId
VALUES
(
    @DictionaryEntryId,
    @ParentParsedId,
    @MeaningTitle,
    @SenseNumber,
    @DomainCode,
    @UsageLabel,
    @Definition,
    @RawFragment,
    SYSUTCDATETIME()
);";

            await using var conn =
                new SqlConnection(_connectionString);

            await conn.OpenAsync(ct);

            var parsedId =
                await conn.ExecuteScalarAsync<long>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            DictionaryEntryId = dictionaryEntryId,
                            ParentParsedId = parentParsedId,
                            MeaningTitle = parsed.MeaningTitle,
                            SenseNumber = parsed.SenseNumber,
                            DomainCode = parsed.Domain,
                            UsageLabel = parsed.UsageLabel,
                            Definition = parsed.Definition,
                            RawFragment = parsed.RawFragment
                        },
                        cancellationToken: ct));

            return parsedId;
        }
    }
}
