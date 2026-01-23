using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using DictionaryImporter.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntryStagingLoader(
        string connectionString,
        ILogger<SqlDictionaryEntryStagingLoader> logger)
        : IStagingLoader
    {
        private static readonly DateTime SqlMinDate = new(1753, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public async Task LoadAsync(IEnumerable<DictionaryEntryStaging> entries, CancellationToken ct)
        {
            if (entries == null) return;

            var list = entries.Where(e => e != null).ToList();
            if (list.Count == 0) return;

            var now = DateTime.UtcNow;

            var sanitized = list
                .Select(e => new DictionaryEntryStaging
                {
                    Word = SafeTruncate(e.Word, 200),
                    NormalizedWord = SafeTruncate(string.IsNullOrWhiteSpace(e.NormalizedWord) ? e.Word : e.NormalizedWord, 200),
                    PartOfSpeech = SafeTruncate(e.PartOfSpeech, 50),
                    Definition = SafeTruncate(e.Definition, 2000),
                    Etymology = SafeTruncate(e.Etymology, 4000),
                    RawFragment = SafeTruncate(e.RawFragment, 8000),
                    SenseNumber = e.SenseNumber,
                    SourceCode = SafeTruncate(string.IsNullOrWhiteSpace(e.SourceCode) ? "UNKNOWN" : e.SourceCode, 30),
                    CreatedUtc = e.CreatedUtc < SqlMinDate ? now : e.CreatedUtc
                })
                .Where(e =>
                    !string.IsNullOrWhiteSpace(e.Word) &&
                    !string.IsNullOrWhiteSpace(e.Definition))
                .ToList();

            if (sanitized.Count == 0) return;

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                ct.ThrowIfCancellationRequested();

                // 1) temp table
                const string createTempSql = """
CREATE TABLE #DictionaryEntryStagingBatch (
    Word            nvarchar(200)   NOT NULL,
    NormalizedWord  nvarchar(200)   NULL,
    PartOfSpeech    nvarchar(50)    NULL,
    Definition      nvarchar(max)  NOT NULL,
    Etymology       nvarchar(max)  NULL,
    SenseNumber     int             NULL,
    SourceCode      nvarchar(30)    NOT NULL,
    CreatedUtc      datetime2       NOT NULL,
    RawFragment     nvarchar(max)  NULL
);
""";

                await conn.ExecuteAsync(new CommandDefinition(
                    createTempSql,
                    transaction: tx,
                    cancellationToken: ct));

                // 2) bulk copy into temp table
                var dt = BuildDataTable(sanitized);

                using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, (SqlTransaction)tx))
                {
                    bulk.DestinationTableName = "#DictionaryEntryStagingBatch";
                    bulk.BatchSize = Helper.MAX_RECORDS_PER_SOURCE;
                    bulk.BulkCopyTimeout = 0;

                    bulk.ColumnMappings.Add("Word", "Word");
                    bulk.ColumnMappings.Add("NormalizedWord", "NormalizedWord");
                    bulk.ColumnMappings.Add("PartOfSpeech", "PartOfSpeech");
                    bulk.ColumnMappings.Add("Definition", "Definition");
                    bulk.ColumnMappings.Add("Etymology", "Etymology");
                    bulk.ColumnMappings.Add("SenseNumber", "SenseNumber");
                    bulk.ColumnMappings.Add("SourceCode", "SourceCode");
                    bulk.ColumnMappings.Add("CreatedUtc", "CreatedUtc");
                    bulk.ColumnMappings.Add("RawFragment", "RawFragment");

                    await bulk.WriteToServerAsync(dt, ct);
                }

                // 3) MERGE (dedupe + insert)
                const string mergeSql = """
MERGE dbo.DictionaryEntry_Staging AS target
USING (
    SELECT
        b.Word,
        b.NormalizedWord,
        b.PartOfSpeech,
        b.Definition,
        b.Etymology,
        b.SenseNumber,
        b.SourceCode,
        b.CreatedUtc,
        b.RawFragment
    FROM #DictionaryEntryStagingBatch b
) AS src
ON (
       target.SourceCode = src.SourceCode
   AND ISNULL(target.SenseNumber, -1) = ISNULL(src.SenseNumber, -1)
   AND target.Word = src.Word
   AND target.Definition = src.Definition
)
WHEN NOT MATCHED BY TARGET THEN
    INSERT (
        Word,
        WordHash,
        NormalizedWord,
        PartOfSpeech,
        Definition,
        DefinitionHash,
        Etymology,
        SenseNumber,
        SourceCode,
        CreatedUtc,
        RawFragment
    )
    VALUES (
        src.Word,
        CONVERT(varchar(64), HASHBYTES('SHA2_256', ISNULL(src.Word,'')), 2),
        src.NormalizedWord,
        src.PartOfSpeech,
        src.Definition,
        CONVERT(varchar(64), HASHBYTES('SHA2_256', ISNULL(src.Definition,'')), 2),
        src.Etymology,
        src.SenseNumber,
        src.SourceCode,
        src.CreatedUtc,
        src.RawFragment
    )
OUTPUT $action;
""";

                // OUTPUT $action returns rows like: INSERT / UPDATE / DELETE (we only do INSERT)
                var actions = await conn.QueryAsync<string>(new CommandDefinition(
                    mergeSql,
                    transaction: tx,
                    cancellationToken: ct));

                var inserted = actions.Count(a => string.Equals(a, "INSERT", StringComparison.OrdinalIgnoreCase));

                await tx.CommitAsync(ct);

                logger.LogInformation(
                    "Inserted staging rows (BATCH) | Inserted={Inserted} | Attempted={Attempted}",
                    inserted, sanitized.Count);
            }
            catch (OperationCanceledException)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }

                logger.LogError(ex,
                    "Failed batch insert staging rows | Attempted={Attempted}",
                    sanitized.Count);
            }
        }

        private static DataTable BuildDataTable(List<DictionaryEntryStaging> rows)
        {
            var dt = new DataTable();

            dt.Columns.Add("Word", typeof(string));
            dt.Columns.Add("NormalizedWord", typeof(string));
            dt.Columns.Add("PartOfSpeech", typeof(string));
            dt.Columns.Add("Definition", typeof(string));
            dt.Columns.Add("Etymology", typeof(string));
            dt.Columns.Add("SenseNumber", typeof(int));
            dt.Columns.Add("SourceCode", typeof(string));
            dt.Columns.Add("CreatedUtc", typeof(DateTime));
            dt.Columns.Add("RawFragment", typeof(string));

            foreach (var r in rows)
            {
                var dr = dt.NewRow();
                dr["Word"] = r.Word ?? string.Empty;
                dr["NormalizedWord"] = (object?)r.NormalizedWord ?? DBNull.Value;
                dr["PartOfSpeech"] = (object?)r.PartOfSpeech ?? DBNull.Value;
                dr["Definition"] = r.Definition ?? string.Empty;
                dr["Etymology"] = (object?)r.Etymology ?? DBNull.Value;
                dr["SenseNumber"] = (object?)r.SenseNumber ?? DBNull.Value;
                dr["SourceCode"] = r.SourceCode ?? "UNKNOWN";
                dr["CreatedUtc"] = r.CreatedUtc.Kind == DateTimeKind.Utc ? r.CreatedUtc : DateTime.SpecifyKind(r.CreatedUtc, DateTimeKind.Utc);
                dr["RawFragment"] = (object?)r.RawFragment ?? DBNull.Value;

                dt.Rows.Add(dr);
            }

            return dt;
        }

        private static string? SafeTruncate(string? text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var t = text.Trim();
            return t.Length <= maxLen ? t : t.Substring(0, maxLen).Trim();
        }
    }
}
