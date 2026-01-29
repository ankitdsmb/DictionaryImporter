using Dapper;
using DictionaryImporter.Common;
using DictionaryImporter.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryStagingLoader : IStagingLoader
{
    private readonly string _connectionString;
    private readonly ILogger<SqlDictionaryEntryStagingLoader> _logger;

    private const int BatchSize = 1500;
    private const int MaxRetries = 3;

    // PERF: Reuse DataTable schema
    private static readonly DataTable _tableTemplate = CreateTableTemplate();

    public SqlDictionaryEntryStagingLoader(
        string connectionString,
        ILogger<SqlDictionaryEntryStagingLoader> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    // ============================================================
    // INGEST
    // ============================================================
    public async Task LoadAsync(
        IEnumerable<DictionaryEntryStaging> entries,
        CancellationToken ct)
    {
        if (entries == null)
            return;

        var now = DateTime.UtcNow;
        var buffer = new List<DictionaryEntryStaging>(BatchSize);

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();

            var sanitized = Sanitize(e, now);
            if (sanitized == null)
                continue;

            buffer.Add(sanitized);

            if (buffer.Count == BatchSize)
            {
                await InsertBatchWithRetryAsync(buffer, ct).ConfigureAwait(false);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
            await InsertBatchWithRetryAsync(buffer, ct).ConfigureAwait(false);
    }

    // ============================================================
    // FINALIZE
    // ============================================================
    public async Task FinalizeAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var empty = _tableTemplate.Clone(); // PERF: no schema rebuild

        var p = new DynamicParameters();
        p.Add("@Entries", empty.AsTableValuedParameter("dbo.DictionaryEntryStagingType"));
        p.Add("@Finalize", 1);

        await conn.ExecuteAsync(
            "dbo.sp_DictionaryEntryStaging_InsertFast",
            p,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 600)
            .ConfigureAwait(false);
    }

    // ============================================================
    // INSERT WITH DEADLOCK RETRY
    // ============================================================
    private async Task InsertBatchWithRetryAsync(
        List<DictionaryEntryStaging> batch,
        CancellationToken ct)
    {
        var attempt = 0;

        while (true)
        {
            try
            {
                await InsertBatchAsync(batch, ct).ConfigureAwait(false);
                return;
            }
            catch (SqlException ex) when (ex.Number == 1205 && attempt < MaxRetries)
            {
                attempt++;
                await Task.Delay(200 * attempt, ct).ConfigureAwait(false);
            }
        }
    }

    // ============================================================
    // INSERT SINGLE BATCH
    // ============================================================
    private async Task InsertBatchAsync(
        List<DictionaryEntryStaging> batch,
        CancellationToken ct)
    {
        var table = BuildDataTable(batch);

        var p = new DynamicParameters();
        p.Add("@Entries", table.AsTableValuedParameter("dbo.DictionaryEntryStagingType"));
        p.Add("@Finalize", 0);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(
            "dbo.sp_DictionaryEntryStaging_InsertFast",
            p,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 120)
            .ConfigureAwait(false);
    }

    // ============================================================
    // SANITIZATION + HASHING
    // ============================================================
    private static DictionaryEntryStaging? Sanitize(
        DictionaryEntryStaging e,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(e.Word) ||
            string.IsNullOrWhiteSpace(e.Definition))
            return null;

        var word = Helper.SqlRepository.SafeTruncateOrEmpty(e.Word, 200);
        var definition = Helper.SqlRepository.SafeTruncateOrEmpty(e.Definition, 2000);

        var wordBytes = Helper.Sha256Bytes(word);
        var defBytes = Helper.Sha256Bytes(definition);

        // PERF: fast guard, avoid SQL retries
        if (wordBytes.Length != 32 || defBytes.Length != 32)
            return null;

        return new DictionaryEntryStaging
        {
            Word = word,
            NormalizedWord = Helper.SqlRepository.SafeTruncateOrEmpty(
                string.IsNullOrWhiteSpace(e.NormalizedWord) ? word : e.NormalizedWord, 200),
            PartOfSpeech = Helper.SqlRepository.SafeTruncateOrNull(e.PartOfSpeech, 50),
            Definition = definition,
            Etymology = Helper.SqlRepository.SafeTruncateOrNull(e.Etymology, 4000),
            RawFragment = Helper.SqlRepository.SafeTruncateOrNull(e.RawFragment, 8000),
            SenseNumber = e.SenseNumber,
            SourceCode = Helper.SqlRepository.SafeTruncateOrEmpty(e.SourceCode, 30),
            CreatedUtc = Helper.SqlRepository.FixSqlMinDateUtc(e.CreatedUtc, now),

            WordHash = Convert.ToHexString(wordBytes),
            DefinitionHash = Convert.ToHexString(defBytes),

            WordHashBytes = wordBytes,
            DefinitionHashBytes = defBytes
        };
    }

    // ============================================================
    // DATATABLE (REUSED SCHEMA)
    // ============================================================
    private static DataTable BuildDataTable(
        List<DictionaryEntryStaging> rows)
    {
        var dt = _tableTemplate.Clone(); // PERF

        foreach (var r in rows)
        {
            var dr = dt.NewRow();

            dr[0] = r.Word;
            dr[1] = r.WordHashBytes;
            dr[2] = r.NormalizedWord;
            dr[3] = (object?)r.PartOfSpeech ?? DBNull.Value;
            dr[4] = r.Definition!;
            dr[5] = r.DefinitionHashBytes;
            dr[6] = (object?)r.Etymology ?? DBNull.Value;
            dr[7] = r.SenseNumber;
            dr[8] = r.SourceCode;
            dr[9] = r.CreatedUtc;
            dr[10] = (object?)r.RawFragment ?? DBNull.Value;

            dt.Rows.Add(dr);
        }

        return dt;
    }

    private static DataTable CreateTableTemplate()
    {
        var dt = new DataTable();

        dt.Columns.Add("Word", typeof(string));
        dt.Columns.Add("WordHash", typeof(byte[]));
        dt.Columns.Add("NormalizedWord", typeof(string));
        dt.Columns.Add("PartOfSpeech", typeof(string));
        dt.Columns.Add("Definition", typeof(string));
        dt.Columns.Add("DefinitionHash", typeof(byte[]));
        dt.Columns.Add("Etymology", typeof(string));
        dt.Columns.Add("SenseNumber", typeof(int));
        dt.Columns.Add("SourceCode", typeof(string));
        dt.Columns.Add("CreatedUtc", typeof(DateTime));
        dt.Columns.Add("RawFragment", typeof(string));

        return dt;
    }
}