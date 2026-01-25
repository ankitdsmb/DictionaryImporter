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
        public async Task LoadAsync(IEnumerable<DictionaryEntryStaging> entries, CancellationToken ct)
        {
            if (entries == null)
                return;

            var list = entries.Where(e => e != null).ToList();
            if (list.Count == 0)
                return;

            var now = DateTime.UtcNow;

            var sanitized = list
                .Select(e => new DictionaryEntryStaging
                {
                    Word = SqlRepositoryHelper.SafeTruncateOrEmpty(e.Word, 200),
                    NormalizedWord = SqlRepositoryHelper.SafeTruncateOrEmpty(
                        string.IsNullOrWhiteSpace(e.NormalizedWord) ? e.Word : e.NormalizedWord, 200),
                    PartOfSpeech = SqlRepositoryHelper.SafeTruncateOrNull(e.PartOfSpeech, 50),
                    Definition = SqlRepositoryHelper.SafeTruncateOrEmpty(e.Definition, 2000),
                    Etymology = SqlRepositoryHelper.SafeTruncateOrNull(e.Etymology, 4000),
                    RawFragment = SqlRepositoryHelper.SafeTruncateOrNull(e.RawFragment, 8000),
                    SenseNumber = e.SenseNumber,
                    SourceCode = SqlRepositoryHelper.SafeTruncateOrEmpty(
                        string.IsNullOrWhiteSpace(e.SourceCode) ? "UNKNOWN" : e.SourceCode, 30),
                    CreatedUtc = SqlRepositoryHelper.FixSqlMinDateUtc(e.CreatedUtc, now)
                })
                .Where(e =>
                    !string.IsNullOrWhiteSpace(e.Word) &&
                    !string.IsNullOrWhiteSpace(e.Definition))
                .ToList();

            if (sanitized.Count == 0)
                return;

            if (sanitized.Count > Helper.MAX_RECORDS_PER_SOURCE)
            {
                sanitized = sanitized
                    .Take(Helper.MAX_RECORDS_PER_SOURCE)
                    .ToList();
            }

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                ct.ThrowIfCancellationRequested();

                const string createTempSql = """
CREATE TABLE #DictionaryEntryStagingBatch (
    Word            nvarchar(200)  NOT NULL,
    NormalizedWord  nvarchar(200)  NULL,
    PartOfSpeech    nvarchar(50)   NULL,
    Definition      nvarchar(max)  NOT NULL,
    Etymology       nvarchar(max)  NULL,
    SenseNumber     int            NULL,
    SourceCode      nvarchar(30)   NOT NULL,
    CreatedUtc      datetime2      NOT NULL,
    RawFragment     nvarchar(max)  NULL
);
""";

                await conn.ExecuteAsync(new CommandDefinition(
                    createTempSql,
                    transaction: tx,
                    cancellationToken: ct));

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

                const string createMergeActionTableSql = """
CREATE TABLE #MergeActions
(
    [action] nvarchar(10) NOT NULL
);
""";

                await conn.ExecuteAsync(new CommandDefinition(
                    createMergeActionTableSql,
                    transaction: tx,
                    cancellationToken: ct));

                const string mergeAndCountSql = """
DECLARE @Inserted INT = 0;

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
OUTPUT $action INTO #MergeActions;

SELECT @Inserted = COUNT(*)
FROM #MergeActions
WHERE [action] = 'INSERT';

SELECT @Inserted;
""";

                var inserted = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    mergeAndCountSql,
                    transaction: tx,
                    cancellationToken: ct));

                await tx.CommitAsync(ct);

                logger.LogInformation(
                    "Inserted staging rows (BATCH) | Inserted={Inserted} | Attempted={Attempted}",
                    inserted,
                    sanitized.Count);
            }
            catch (OperationCanceledException)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(CancellationToken.None); } catch { }

                logger.LogError(
                    ex,
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
                dr["CreatedUtc"] = SqlRepositoryHelper.EnsureUtc(r.CreatedUtc);
                dr["RawFragment"] = (object?)r.RawFragment ?? DBNull.Value;

                dt.Rows.Add(dr);
            }

            return dt;
        }
    }
}
