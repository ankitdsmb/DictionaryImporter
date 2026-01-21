// File: Infrastructure/Persistence/SqlDictionaryEntryEtymologyWriter.cs
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using DictionaryImporter.Core.Persistence;
using DictionaryImporter.Domain.Models;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public class SqlDictionaryEntryEtymologyWriter(
        string connectionString,
        ILogger<SqlDictionaryEntryEtymologyWriter> logger)
        : IEntryEtymologyWriter
    {
        public async Task WriteAsync(DictionaryEntryEtymology etymology, CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.DictionaryEntryEtymology (
                    DictionaryEntryId, EtymologyText, LanguageCode,
                    SourceCode, CreatedUtc
                ) VALUES (
                    @DictionaryEntryId, @EtymologyText, @LanguageCode,
                    @SourceCode, SYSUTCDATETIME()
                );
                """;

            var parameters = new
            {
                etymology.DictionaryEntryId,
                etymology.EtymologyText,
                etymology.LanguageCode,
                etymology.SourceCode
            };

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, cancellationToken: ct));

            logger.LogDebug("Wrote etymology for DictionaryEntryId={EntryId}", etymology.DictionaryEntryId);
        }
    }
}