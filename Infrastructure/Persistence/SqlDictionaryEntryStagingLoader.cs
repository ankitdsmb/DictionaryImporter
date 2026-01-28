using DictionaryImporter.Common;

namespace DictionaryImporter.Infrastructure.Persistence;

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
            .Select(e =>
            {
                var word = Helper.SqlRepository.SafeTruncateOrEmpty(e.Word, 200);
                var def = Helper.SqlRepository.SafeTruncateOrEmpty(e.Definition, 2000);

                return new DictionaryEntryStaging
                {
                    Word = word,
                    WordHash = Helper.Sha256(word),

                    NormalizedWord = Helper.SqlRepository.SafeTruncateOrEmpty(
                        string.IsNullOrWhiteSpace(e.NormalizedWord) ? word : e.NormalizedWord, 200),

                    PartOfSpeech = Helper.SqlRepository.SafeTruncateOrNull(e.PartOfSpeech, 50),

                    Definition = def,
                    DefinitionHash = Helper.Sha256(def),

                    Etymology = Helper.SqlRepository.SafeTruncateOrNull(e.Etymology, 4000),
                    RawFragment = Helper.SqlRepository.SafeTruncateOrNull(e.RawFragment, 8000),

                    SenseNumber = e.SenseNumber,

                    SourceCode = Helper.SqlRepository.SafeTruncateOrEmpty(
                        string.IsNullOrWhiteSpace(e.SourceCode) ? "UNKNOWN" : e.SourceCode, 30),

                    CreatedUtc = Helper.SqlRepository.FixSqlMinDateUtc(e.CreatedUtc, now)
                };
            })
            .Where(e =>
                !string.IsNullOrWhiteSpace(e.Word) &&
                !string.IsNullOrWhiteSpace(e.Definition))
            .Take(Helper.MAX_RECORDS_PER_SOURCE)
            .ToList();

        if (sanitized.Count == 0)
            return;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            ct.ThrowIfCancellationRequested();

            // =========================
            // TEMP TABLE + INDEX
            // =========================
            const string createTempSql = """
                CREATE TABLE #DictionaryEntryStagingBatch
                (
                    Word            nvarchar(200) NOT NULL,
                    WordHash        varchar(64)   NOT NULL,
                    NormalizedWord  nvarchar(200) NULL,
                    PartOfSpeech    nvarchar(50)  NULL,
                    Definition      nvarchar(max) NOT NULL,
                    DefinitionHash  varchar(64)   NOT NULL,
                    Etymology       nvarchar(max) NULL,
                    SenseNumber     int           NULL,
                    SourceCode      nvarchar(30)  NOT NULL,
                    CreatedUtc      datetime2     NOT NULL,
                    RawFragment     nvarchar(max) NULL
                );

                CREATE NONCLUSTERED INDEX IX_StagingBatch_Lookup
                ON #DictionaryEntryStagingBatch
                (SourceCode, WordHash, DefinitionHash, SenseNumber);
                """;

            await conn.ExecuteAsync(
                new CommandDefinition(createTempSql, transaction: tx, cancellationToken: ct));

            // =========================
            // BULK COPY
            // =========================
            var dt = BuildDataTable(sanitized);

            using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.TableLock, (SqlTransaction)tx))
            {
                bulk.DestinationTableName = "#DictionaryEntryStagingBatch";
                bulk.BatchSize = Helper.MAX_RECORDS_PER_SOURCE;
                bulk.BulkCopyTimeout = 0;

                bulk.ColumnMappings.Add("Word", "Word");
                bulk.ColumnMappings.Add("WordHash", "WordHash");
                bulk.ColumnMappings.Add("NormalizedWord", "NormalizedWord");
                bulk.ColumnMappings.Add("PartOfSpeech", "PartOfSpeech");
                bulk.ColumnMappings.Add("Definition", "Definition");
                bulk.ColumnMappings.Add("DefinitionHash", "DefinitionHash");
                bulk.ColumnMappings.Add("Etymology", "Etymology");
                bulk.ColumnMappings.Add("SenseNumber", "SenseNumber");
                bulk.ColumnMappings.Add("SourceCode", "SourceCode");
                bulk.ColumnMappings.Add("CreatedUtc", "CreatedUtc");
                bulk.ColumnMappings.Add("RawFragment", "RawFragment");

                await bulk.WriteToServerAsync(dt, ct);
            }

            // =========================
            // FAST INSERT (NO MERGE)
            // =========================
            const string insertSql = """
                INSERT INTO dbo.DictionaryEntry_Staging
                (
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
                SELECT
                    b.Word,
                    b.WordHash,
                    b.NormalizedWord,
                    b.PartOfSpeech,
                    b.Definition,
                    b.DefinitionHash,
                    b.Etymology,
                    b.SenseNumber,
                    b.SourceCode,
                    b.CreatedUtc,
                    b.RawFragment
                FROM #DictionaryEntryStagingBatch b
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.DictionaryEntry_Staging t
                    WHERE t.SourceCode = b.SourceCode
                      AND t.WordHash = b.WordHash
                      AND t.DefinitionHash = b.DefinitionHash
                      AND ISNULL(t.SenseNumber, -1) = ISNULL(b.SenseNumber, -1)
                );

                SELECT @@ROWCOUNT;
                """;

            var inserted = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(insertSql, transaction: tx, cancellationToken: ct));

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
        dt.Columns.Add("WordHash", typeof(string));
        dt.Columns.Add("NormalizedWord", typeof(string));
        dt.Columns.Add("PartOfSpeech", typeof(string));
        dt.Columns.Add("Definition", typeof(string));
        dt.Columns.Add("DefinitionHash", typeof(string));
        dt.Columns.Add("Etymology", typeof(string));
        dt.Columns.Add("SenseNumber", typeof(int));
        dt.Columns.Add("SourceCode", typeof(string));
        dt.Columns.Add("CreatedUtc", typeof(DateTime));
        dt.Columns.Add("RawFragment", typeof(string));

        foreach (var r in rows)
        {
            var dr = dt.NewRow();
            dr["Word"] = r.Word;
            dr["WordHash"] = r.WordHash;
            dr["NormalizedWord"] = (object?)r.NormalizedWord ?? DBNull.Value;
            dr["PartOfSpeech"] = (object?)r.PartOfSpeech ?? DBNull.Value;
            dr["Definition"] = r.Definition;
            dr["DefinitionHash"] = r.DefinitionHash;
            dr["Etymology"] = (object?)r.Etymology ?? DBNull.Value;
            dr["SenseNumber"] = (object?)r.SenseNumber ?? DBNull.Value;
            dr["SourceCode"] = r.SourceCode;
            dr["CreatedUtc"] = Helper.SqlRepository.EnsureUtc(r.CreatedUtc);
            dr["RawFragment"] = (object?)r.RawFragment ?? DBNull.Value;

            dt.Rows.Add(dr);
        }

        return dt;
    }
}