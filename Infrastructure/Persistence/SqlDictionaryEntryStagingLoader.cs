using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Infrastructure.FragmentStore;

namespace DictionaryImporter.Infrastructure.Persistence;

public sealed class SqlDictionaryEntryStagingLoader(string connectionString, ILogger<SqlDictionaryEntryStagingLoader> logger) : IStagingLoader
{
    private readonly ILogger<SqlDictionaryEntryStagingLoader> _logger = logger;

    private int _adaptiveBatchSize = 2000;

    private const int MinBatchSize = 500;
    private const int MaxBatchSize = 4000;
    private const int MaxRetries = 3;

    private const bool UseBulkCopy = true;

    private static readonly ConcurrentDictionary<string, bool> EnsuredSources = new();

    private static readonly DataTable TableTemplate = CreateTableTemplate();

    public async Task LoadAsync(
        IEnumerable<DictionaryEntryStaging> entries,
        CancellationToken ct)
    {
        if (entries == null)
            return;

        var now = DateTime.UtcNow;
        var buffer = new List<DictionaryEntryStaging>(_adaptiveBatchSize);

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();

            var sanitized = Sanitize(e, now);
            if (sanitized == null)
                continue;

            buffer.Add(sanitized);

            if (buffer.Count >= _adaptiveBatchSize)
            {
                await FlushAdaptiveAsync(buffer, ct).ConfigureAwait(false);
                buffer.Clear();
            }
        }

        if (buffer.Count > 0)
        {
            await FlushAdaptiveAsync(buffer, ct).ConfigureAwait(false);
            buffer.Clear();
        }
    }

    private async Task FlushAdaptiveAsync(
        List<DictionaryEntryStaging> batch,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (UseBulkCopy)
            await BulkInsertAsync(batch, ct).ConfigureAwait(false);
        else
            await InsertBatchWithRetryAsync(batch, ct).ConfigureAwait(false);

        sw.Stop();
        AdjustBatchSize(sw.ElapsedMilliseconds);
    }

    private void AdjustBatchSize(long elapsedMs)
    {
        if (elapsedMs < 300 && _adaptiveBatchSize < MaxBatchSize)
            _adaptiveBatchSize += 250;
        else if (elapsedMs > 1200 && _adaptiveBatchSize > MinBatchSize)
            _adaptiveBatchSize -= 250;
    }

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

    private async Task InsertBatchAsync(
        List<DictionaryEntryStaging> batch,
        CancellationToken ct)
    {
        var table = BuildDataTable(batch);

        var p = new DynamicParameters();
        p.Add("@Entries", table.AsTableValuedParameter("dbo.DictionaryEntryStagingType"));
        p.Add("@Finalize", 0);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(
            "dbo.sp_DictionaryEntryStaging_InsertFast",
            p,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 120)
            .ConfigureAwait(false);
    }

    private async Task BulkInsertAsync(
        List<DictionaryEntryStaging> batch,
        CancellationToken ct)
    {
        var sourceCode = batch[0].SourceCode;

        await EnsureSourceTableAsync(sourceCode, ct).ConfigureAwait(false);

        var tableName = $"dbo.DictionaryEntry_Staging_Source_{sourceCode}";
        var dt = BuildDataTable(batch);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        using var bulk = new SqlBulkCopy(
            conn,
            SqlBulkCopyOptions.TableLock,
            null)
        {
            DestinationTableName = tableName,
            BatchSize = dt.Rows.Count,
            BulkCopyTimeout = 300
        };

        await bulk.WriteToServerAsync(dt, ct).ConfigureAwait(false);
    }

    private async Task EnsureSourceTableAsync(
        string sourceCode,
        CancellationToken ct)
    {
        if (EnsuredSources.ContainsKey(sourceCode))
            return;

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(
            "dbo.sp_DictionaryEntryStaging_EnsureSourceTable",
            new { SourceCode = sourceCode },
            commandType: CommandType.StoredProcedure)
            .ConfigureAwait(false);

        EnsuredSources[sourceCode] = true;
    }

    private static DictionaryEntryStaging? Sanitize(DictionaryEntryStaging e, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(e.Word) ||
            string.IsNullOrWhiteSpace(e.Definition))
            return null;

        var word = Helper.SqlRepository.SafeTruncateOrEmpty(e.Word, 200);
        var definition = Helper.SqlRepository.SafeTruncateOrEmpty(e.Definition, 2000);

        var wordBytes = Helper.Sha256Bytes(word);
        var defBytes = Helper.Sha256Bytes(definition);

        if (wordBytes.Length != 32 || defBytes.Length != 32)
            return null;

        return new DictionaryEntryStaging
        {
            Word = word,
            Definition = definition,
            SenseNumber = e.SenseNumber,
            NormalizedWord = Helper.SqlRepository.SafeTruncateOrEmpty(string.IsNullOrWhiteSpace(e.NormalizedWord) ? word : e.NormalizedWord, 200),
            PartOfSpeech = Helper.SqlRepository.SafeTruncateOrNull(e.PartOfSpeech, 50),
            Etymology = Helper.SqlRepository.SafeTruncateOrNull(e.Etymology, 4000),
            RawFragment = RawFragments.Save(e.SourceCode, e.RawFragmentLine, Encoding.UTF8, word),
            SourceCode = Helper.SqlRepository.SafeTruncateOrEmpty(e.SourceCode, 30),
            CreatedUtc = Helper.SqlRepository.FixSqlMinDateUtc(e.CreatedUtc, now),
            WordHashBytes = wordBytes,
            DefinitionHashBytes = defBytes
        };
    }

    private static DataTable BuildDataTable(
        List<DictionaryEntryStaging> rows)
    {
        var dt = TableTemplate.Clone();

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