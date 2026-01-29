using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Core.Orchestration.Concurrency;

public sealed class InMemoryBatchCollector(
    string connectionString,
    ILogger<InMemoryBatchCollector> logger,
    IOptions<BatchProcessingSettings> settings)
    : IBatchProcessedDataCollector, IDisposable
{
    private readonly string _connectionString = connectionString;
    private readonly BatchProcessingSettings _settings = settings?.Value ?? new();

    private readonly ThreadLocal<List<ParsedBatchItem>> _localItems =
        new(() => new List<ParsedBatchItem>(64), trackAllValues: true);

    private readonly ConcurrentQueue<List<ParsedBatchItem>> _flushQueue = new();
    private int _count;
    private bool _disposed;

    public int BatchSize => _settings.BatchSize;

    // =========================================================
    // ADD ROOT
    // =========================================================
    public Task AddParsedDefinitionAsync(long entryId, ParsedDefinition parsed, string sourceCode)
    {
        var list = _localItems.Value!;
        list.Add(new ParsedBatchItem(entryId, parsed, sourceCode));

        if (Interlocked.Increment(ref _count) >= _settings.BatchSize)
            Seal(list);

        return Task.CompletedTask;
    }

    // =========================================================
    // CHILD ADDERS
    // =========================================================
    public Task AddExampleAsync(long _, string text, string __)
        => Add(i => i.Examples.Add(text));

    public Task AddSynonymsAsync(long _, IEnumerable<string> s, string __)
        => Add(i => i.Synonyms.AddRange(s));

    public Task AddAliasAsync(long _, string a, long __, string ___)
        => Add(i => i.Aliases.Add(a));

    public Task AddCrossReferenceAsync(long _, CrossReference cr, string __)
        => Add(i => i.CrossReferences.Add(cr));

    public Task AddEtymologyAsync(DictionaryEntryEtymology et)
        => Add(i => i.Etymologies.Add(et));

    private Task Add(Action<ParsedBatchItem> action)
    {
        var list = _localItems.Value!;
        if (list.Count > 0)
            action(list[^1]);

        return Task.CompletedTask;
    }

    // =========================================================
    // BATCH SEAL + FLUSH
    // =========================================================
    private void Seal(List<ParsedBatchItem> list)
    {
        if (Interlocked.Exchange(ref _count, 0) < _settings.BatchSize)
            return;

        _flushQueue.Enqueue(list);
        _localItems.Value = new List<ParsedBatchItem>(64);
        _ = Task.Run(FlushAsync);
    }

    public async Task FlushBatchAsync(CancellationToken ct)
    {
        foreach (var list in _localItems.Values)
        {
            if (list.Count > 0)
                _flushQueue.Enqueue(list);
        }

        await FlushAsync();
    }

    private async Task FlushAsync()
    {
        while (_flushQueue.TryDequeue(out var batch))
            await FlushInternal(batch);
    }

    private async Task FlushInternal(List<ParsedBatchItem> batch)
    {
        // 🔒 SNAPSHOT ENTIRE BATCH (deep copy)
        var frozen = FreezeBatch(batch);

        // Assign BatchItemId deterministically
        var id = 1;
        foreach (var item in frozen)
            item.BatchItemId = id++;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var p = new DynamicParameters();

        p.Add("@Parsed",
            BuildParsed(frozen)
                .AsTableValuedParameter("dbo.DictionaryEntryParsedBatchType"));

        p.Add("@Aliases",
            BuildAliases(frozen)
                .AsTableValuedParameter("dbo.DictionaryEntryAliasBatchType"));

        p.Add("@Synonyms",
            BuildSynonyms(frozen)
                .AsTableValuedParameter("dbo.DictionaryEntrySynonymBatchType"));

        p.Add("@Examples",
            BuildExamples(frozen)
                .AsTableValuedParameter("dbo.DictionaryEntryExampleBatchType"));

        p.Add("@CrossRefs",
            BuildCrossRefs(frozen)
                .AsTableValuedParameter("dbo.DictionaryEntryCrossReferenceBatchType"));

        p.Add("@Etymologies",
            BuildEtymologies(frozen)
                .AsTableValuedParameter("dbo.DictionaryEntryEtymologyBatchType"));

        await conn.ExecuteAsync(
            "dbo.sp_DictionaryEntryParsed_InsertCompleteBatch",
            p,
            commandType: CommandType.StoredProcedure,
            commandTimeout: 120);
    }

    private static List<ParsedBatchItem> FreezeBatch(IEnumerable<ParsedBatchItem> source)
    {
        var frozen = new List<ParsedBatchItem>();

        foreach (var i in source)
        {
            var copy = new ParsedBatchItem(
                i.DictionaryEntryId,
                new ParsedDefinition
                {
                    MeaningTitle = i.MeaningTitle,
                    Definition = i.Definition,
                    RawFragment = i.RawFragment,
                    SenseNumber = i.SenseNumber,
                    Domain = i.Domain,
                    UsageLabel = i.UsageLabel,
                    HasNonEnglishText = i.HasNonEnglishText,
                    NonEnglishTextId = i.NonEnglishTextId
                },
                i.SourceCode
            );

            // 🔒 SNAPSHOT ALL CHILD COLLECTIONS
            copy.Aliases.AddRange(i.Aliases);
            copy.Synonyms.AddRange(i.Synonyms);
            copy.Examples.AddRange(i.Examples);
            copy.CrossReferences.AddRange(i.CrossReferences);
            copy.Etymologies.AddRange(i.Etymologies);

            frozen.Add(copy);
        }

        return frozen;
    }

    private static DataTable BuildParsed(IEnumerable<ParsedBatchItem> items)
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

        foreach (var i in items)
        {
            t.Rows.Add(
                i.BatchItemId,
                i.DictionaryEntryId,
                DBNull.Value,
                i.MeaningTitle,
                i.Definition,
                i.RawFragment,
                i.SenseNumber,
                i.Domain,
                i.UsageLabel,
                i.HasNonEnglishText,
                i.NonEnglishTextId ?? (object)DBNull.Value,
                i.SourceCode
            );
        }

        return t;
    }

    private static DataTable BuildAliases(IEnumerable<ParsedBatchItem> items)
    {
        var t = new DataTable();
        t.Columns.Add("BatchItemId", typeof(int));
        t.Columns.Add("AliasText", typeof(string));

        foreach (var i in items)
            foreach (var a in i.Aliases)
                t.Rows.Add(i.BatchItemId, a);

        return t;
    }

    private static DataTable BuildSynonyms(IEnumerable<ParsedBatchItem> items)
    {
        var t = new DataTable();
        t.Columns.Add("BatchItemId", typeof(int));
        t.Columns.Add("SynonymText", typeof(string));

        foreach (var i in items)
            foreach (var s in i.Synonyms)
                t.Rows.Add(i.BatchItemId, s);

        return t;
    }

    private static DataTable BuildExamples(IEnumerable<ParsedBatchItem> items)
    {
        var t = new DataTable();
        t.Columns.Add("BatchItemId", typeof(int));
        t.Columns.Add("ExampleText", typeof(string));

        foreach (var i in items)
            foreach (var e in i.Examples)
                t.Rows.Add(i.BatchItemId, e);

        return t;
    }

    private static DataTable BuildCrossRefs(IEnumerable<ParsedBatchItem> items)
    {
        var t = new DataTable();
        t.Columns.Add("BatchItemId", typeof(int));
        t.Columns.Add("TargetWord", typeof(string));
        t.Columns.Add("ReferenceType", typeof(string));

        foreach (var i in items)
            foreach (var c in i.CrossReferences)
                t.Rows.Add(i.BatchItemId, c.TargetWord, c.ReferenceType ?? "see");

        return t;
    }

    private static DataTable BuildEtymologies(IEnumerable<ParsedBatchItem> items)
    {
        var t = new DataTable();
        t.Columns.Add("BatchItemId", typeof(long));
        t.Columns.Add("DictionaryEntryId", typeof(long));
        t.Columns.Add("EtymologyText", typeof(string));
        t.Columns.Add("LanguageCode", typeof(string));
        t.Columns.Add("HasNonEnglishText", typeof(bool));
        t.Columns.Add("NonEnglishTextId", typeof(long));
        t.Columns.Add("SourceCode", typeof(string));

        foreach (var i in items)
            foreach (var e in i.Etymologies)
                t.Rows.Add(
                    i.BatchItemId,
                    e.DictionaryEntryId,
                    e.EtymologyText,
                    e.LanguageCode,
                    e.HasNonEnglishText,
                    e.NonEnglishTextId ?? (object)DBNull.Value,
                    e.SourceCode
                );

        return t;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _localItems.Dispose();
        _disposed = true;
    }

    // =========================================================
    // INNER MODEL
    // =========================================================
    internal sealed class ParsedBatchItem
    {
        public ParsedBatchItem(long id, ParsedDefinition p, string s)
        {
            DictionaryEntryId = id;
            MeaningTitle = p.MeaningTitle ?? string.Empty;
            Definition = p.Definition ?? string.Empty;
            RawFragment = p.RawFragment ?? string.Empty;
            SenseNumber = p.SenseNumber;
            Domain = p.Domain;
            UsageLabel = p.UsageLabel;
            HasNonEnglishText = p.HasNonEnglishText;
            NonEnglishTextId = p.NonEnglishTextId;
            SourceCode = s;
        }

        public int BatchItemId; // 🔑 CRITICAL
        public long DictionaryEntryId;
        public string MeaningTitle;
        public string Definition;
        public string RawFragment;
        public int SenseNumber;
        public string? Domain;
        public string? UsageLabel;
        public bool HasNonEnglishText;
        public long? NonEnglishTextId;
        public string SourceCode;

        public List<string> Aliases = new();
        public List<string> Synonyms = new();
        public List<string> Examples = new();
        public List<CrossReference> CrossReferences = new();
        public List<DictionaryEntryEtymology> Etymologies = new();
    }
}