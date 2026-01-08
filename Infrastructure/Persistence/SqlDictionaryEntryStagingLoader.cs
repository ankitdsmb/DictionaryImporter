using Dapper;
using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryStagingLoader
        : IStagingLoader
    {
        private readonly string _cs;
        private readonly ILogger<SqlDictionaryEntryStagingLoader> _logger;

        public SqlDictionaryEntryStagingLoader(
            string connectionString,
            ILogger<SqlDictionaryEntryStagingLoader> logger)
        {
            _cs = connectionString;
            _logger = logger;
        }

        public async Task LoadAsync(
            IEnumerable<DictionaryEntryStaging> entries,
            CancellationToken ct)
        {
            var list = entries.ToList();

            if (list.Count == 0)
                return;

            const string sql = """
            INSERT INTO dbo.DictionaryEntry_Staging
            (Word, NormalizedWord, PartOfSpeech, Definition,
             Etymology, SenseNumber, SourceCode, CreatedUtc)
            VALUES
            (@Word, @NormalizedWord, @PartOfSpeech, @Definition,
             @Etymology, @SenseNumber, @SourceCode, @CreatedUtc);
            """;

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                await conn.ExecuteAsync(
                    sql,
                    list,
                    transaction: tx);

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Committed batch of {Count} staging rows",
                    list.Count);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(
                    "Rolled back batch of {Count} rows",
                    list.Count);
                throw;
            }
        }
    }
}
