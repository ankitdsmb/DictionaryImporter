using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.Orchestration;

public sealed class InMemoryBatchCollector(
    string connectionString,
    ILogger<InMemoryBatchCollector> logger,
    IOptions<BatchProcessingSettings> settings)
    : IBatchProcessedDataCollector, IDisposable
{
    private readonly string _connectionString = connectionString;
    private readonly ILogger<InMemoryBatchCollector> _logger = logger;
    private readonly BatchProcessingSettings _settings = settings?.Value ?? new();

    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;
    private int _nextBatchItemId = 1;

    private readonly List<ParsedBatchItem> _items = new();

    public int BatchSize => _settings.BatchSize;

    // =========================
    // ADD ROOT
    // =========================
    public async Task AddParsedDefinitionAsync(long dictionaryEntryId, ParsedDefinition parsed, string sourceCode)
    {
        await _lock.WaitAsync();
        try
        {
            _items.Add(new ParsedBatchItem
            {
                BatchItemId = _nextBatchItemId++,
                DictionaryEntryId = dictionaryEntryId,
                ParentParsedId = parsed.ParentParsedId > 0 ? parsed.ParentParsedId : null,
                MeaningTitle = parsed.MeaningTitle ?? string.Empty,
                Definition = parsed.Definition ?? string.Empty,
                RawFragment = parsed.RawFragment ?? string.Empty,
                SenseNumber = parsed.SenseNumber,
                Domain = parsed.Domain,
                UsageLabel = parsed.UsageLabel,
                HasNonEnglishText = parsed.HasNonEnglishText,
                NonEnglishTextId = parsed.NonEnglishTextId,
                SourceCode = sourceCode
            });

            if (_items.Count >= _settings.BatchSize)
                await FlushBatchAsync(CancellationToken.None);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddExampleAsync(long _, string exampleText, string __)
        => await AddTextAsync(i => i.Examples.Add(Normalize(exampleText)));

    public async Task AddSynonymsAsync(long _, IEnumerable<string> synonyms, string __)
        => await AddTextAsync(i => i.Synonyms.AddRange(synonyms.Select(Normalize)));

    public async Task AddAliasAsync(long _, string alias, long __, string ___)
        => await AddTextAsync(i => i.Aliases.Add(Normalize(alias)));

    public async Task AddCrossReferenceAsync(long _, CrossReference cr, string __)
        => await AddTextAsync(i => i.CrossReferences.Add(cr));

    public async Task AddEtymologyAsync(DictionaryEntryEtymology et)
        => await AddTextAsync(i => i.Etymologies.Add(et));

    private async Task AddTextAsync(Action<ParsedBatchItem> action)
    {
        await _lock.WaitAsync();
        try
        {
            if (_items.Count == 0) return;
            action(_items[^1]);
        }
        finally
        {
            _lock.Release();
        }
    }

    // =========================
    // FLUSH
    // =========================
    public async Task FlushBatchAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_items.Count == 0) return;

            var table = BuildBatchTable(_items);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var p = new DynamicParameters();
            p.Add("@Rows", table.AsTableValuedParameter("dbo.DictionaryEntryParsedBatchType"));

            await conn.ExecuteAsync(
                "dbo.sp_DictionaryEntryParsed_InsertCompleteBatch",
                p,
                commandType: CommandType.StoredProcedure,
                commandTimeout: 120);

            _items.Clear();
            _nextBatchItemId = 1;
        }
        finally
        {
            _lock.Release();
        }
    }

    // =========================
    // BUILD TVP
    // =========================
    private static DataTable BuildBatchTable(IEnumerable<ParsedBatchItem> items)
    {
        var t = new DataTable();

        t.Columns.Add("BatchItemId", typeof(int));
        t.Columns.Add("DictionaryEntryId", typeof(long));
        t.Columns.Add("ParentParsedId", typeof(long));
        t.Columns.Add("MeaningTitle", typeof(string));
        t.Columns.Add("Definition", typeof(string));
        t.Columns.Add("RawFragment", typeof(string));
        t.Columns.Add("SenseNumber", typeof(int));
        t.Columns.Add("Domain", typeof(string));
        t.Columns.Add("UsageLabel", typeof(string));
        t.Columns.Add("HasNonEnglishText", typeof(bool));
        t.Columns.Add("NonEnglishTextId", typeof(long));
        t.Columns.Add("SourceCode", typeof(string));

        t.Columns.Add("AliasesJson", typeof(string));
        t.Columns.Add("SynonymsJson", typeof(string));
        t.Columns.Add("ExamplesJson", typeof(string));
        t.Columns.Add("CrossReferencesJson", typeof(string));
        t.Columns.Add("EtymologiesJson", typeof(string));

        foreach (var i in items)
        {
            t.Rows.Add(
                i.BatchItemId,
                i.DictionaryEntryId,
                i.ParentParsedId ?? (object)DBNull.Value,
                i.MeaningTitle,
                i.Definition,
                i.RawFragment,
                i.SenseNumber,
                i.Domain ?? (object)DBNull.Value,
                i.UsageLabel ?? (object)DBNull.Value,
                i.HasNonEnglishText,
                i.NonEnglishTextId ?? (object)DBNull.Value,
                i.SourceCode,
                ToJsonArray(i.Aliases),
                ToJsonArray(i.Synonyms),
                ToJsonArray(i.Examples),
                ToJsonArray(i.CrossReferences),
                ToJsonArray(i.Etymologies)
            );
        }

        return t;
    }

    private static string ToJsonArray<T>(IEnumerable<T> values)
        => values == null ? "[]" : JsonSerializer.Serialize(values);

    private static string Normalize(string text)
        => text?.Trim().Trim('"') ?? string.Empty;

    public void Dispose()
    {
        if (_disposed) return;
        _lock.Dispose();
        _disposed = true;
    }

    internal sealed class ParsedBatchItem
    {
        public int BatchItemId { get; init; }
        public long DictionaryEntryId { get; init; }
        public long? ParentParsedId { get; init; }
        public string MeaningTitle { get; init; }
        public string Definition { get; init; }
        public string RawFragment { get; init; }
        public int SenseNumber { get; init; }
        public string Domain { get; init; }
        public string UsageLabel { get; init; }
        public bool HasNonEnglishText { get; init; }
        public long? NonEnglishTextId { get; init; }
        public string SourceCode { get; init; }

        public List<string> Aliases { get; } = new();
        public List<string> Synonyms { get; } = new();
        public List<string> Examples { get; } = new();
        public List<CrossReference> CrossReferences { get; } = new();
        public List<DictionaryEntryEtymology> Etymologies { get; } = new();
    }
}