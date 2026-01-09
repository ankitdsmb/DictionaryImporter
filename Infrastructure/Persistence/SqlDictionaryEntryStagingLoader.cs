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

        // SQL Server datetime minimum
        private static readonly DateTime SqlMinDate =
            new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

            var now = DateTime.UtcNow;

            // ------------------------------------------------------------
            // FIX: Project into NEW instances (init-only safe)
            // ------------------------------------------------------------
            var sanitized = list.Select(e =>
                new DictionaryEntryStaging
                {
                    Word = e.Word,
                    NormalizedWord = e.NormalizedWord,
                    PartOfSpeech = e.PartOfSpeech,
                    Definition = e.Definition,
                    Etymology = e.Etymology,
                    SenseNumber = e.SenseNumber,
                    SourceCode = e.SourceCode,
                    CreatedUtc = e.CreatedUtc < SqlMinDate
                        ? now
                        : e.CreatedUtc
                })
                .ToList();

            const string sql = """
                INSERT INTO dbo.DictionaryEntry_Staging
                (
                    Word,
                    NormalizedWord,
                    PartOfSpeech,
                    Definition,
                    Etymology,
                    SenseNumber,
                    SourceCode,
                    CreatedUtc
                )
                VALUES
                (
                    @Word,
                    @NormalizedWord,
                    @PartOfSpeech,
                    @Definition,
                    @Etymology,
                    @SenseNumber,
                    @SourceCode,
                    @CreatedUtc
                );
                """;

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                await conn.ExecuteAsync(
                    sql,
                    sanitized,
                    transaction: tx);

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "Committed batch of {Count} staging rows",
                    sanitized.Count);
            }
            catch
            {
                await tx.RollbackAsync(ct);

                _logger.LogError(
                    "Rolled back batch of {Count} staging rows",
                    sanitized.Count);

                throw;
            }
        }
    }
}
