using System.Collections.Concurrent;
using System.Data;
using System.Runtime.CompilerServices;
using DictionaryImporter.Common;
using DictionaryImporter.Infrastructure.Source;
using Dapper;
using DictionaryImporter.Core.Domain.Models;
using Microsoft.Data.SqlClient;
using DictionaryImporter.Core.Orchestration.Concurrency;

namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class DictionaryParsedDefinitionProcessor : IParsedDefinitionProcessor
{
    private readonly string _connectionString;
    private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;

    private readonly IDictionaryDefinitionParserResolver _parserResolver;
    private readonly IExampleExtractorRegistry _exampleExtractors;
    private readonly ISynonymExtractorRegistry _synonymExtractors;
    private readonly IEtymologyExtractorRegistry _etymologyExtractors;

    private readonly SqlParsedDefinitionWriter _parsedWriter;
    private readonly IDictionaryEntryExampleWriter _exampleWriter;
    private readonly IDictionaryEntrySynonymWriter _synonymWriter;
    private readonly IDictionaryEntryAliasWriter _aliasWriter;
    private readonly IDictionaryEntryVariantWriter _variantWriter;
    private readonly IDictionaryEntryCrossReferenceWriter _crossRefWriter;
    private readonly IEntryEtymologyWriter _etymologyWriter;

    private readonly IDictionaryTextFormatter _formatter;
    private readonly IGrammarEnrichedTextService _grammarText;
    private readonly ILanguageDetectionService _languageDetection;
    private readonly INonEnglishTextStorage _nonEnglishStorage;
    private readonly IOcrArtifactNormalizer _ocrNormalizer;
    private readonly IDefinitionNormalizer _definitionNormalizer;

    private readonly IBatchProcessedDataCollector _batchCollector;
    private readonly BatchProcessingSettings _batchSettings;
    private readonly bool _useBatch;

    public DictionaryParsedDefinitionProcessor(
        string connectionString,
        IDictionaryDefinitionParserResolver parserResolver,
        SqlParsedDefinitionWriter parsedWriter,
        IDictionaryEntryCrossReferenceWriter crossRefWriter,
        IDictionaryEntryAliasWriter aliasWriter,
        IEntryEtymologyWriter etymologyWriter,
        IDictionaryEntryVariantWriter variantWriter,
        IDictionaryEntryExampleWriter exampleWriter,
        IExampleExtractorRegistry exampleExtractorRegistry,
        ISynonymExtractorRegistry synonymExtractorRegistry,
        IDictionaryEntrySynonymWriter synonymWriter,
        IEtymologyExtractorRegistry etymologyExtractorRegistry,
        IDictionaryTextFormatter formatter,
        IGrammarEnrichedTextService grammarText,
        ILanguageDetectionService languageDetectionService,
        INonEnglishTextStorage nonEnglishTextStorage,
        IOcrArtifactNormalizer ocrNormalizer,
        IDefinitionNormalizer definitionNormalizer,
        ILogger<DictionaryParsedDefinitionProcessor> logger,
        IOptions<BatchProcessingSettings> batchSettings = null,
        IBatchProcessedDataCollector batchCollector = null)
    {
        _connectionString = connectionString;
        _logger = logger;

        _parserResolver = parserResolver;
        _parsedWriter = parsedWriter;
        _crossRefWriter = crossRefWriter;
        _aliasWriter = aliasWriter;
        _etymologyWriter = etymologyWriter;
        _variantWriter = variantWriter;
        _exampleWriter = exampleWriter;
        _synonymWriter = synonymWriter;

        _exampleExtractors = exampleExtractorRegistry;
        _synonymExtractors = synonymExtractorRegistry;
        _etymologyExtractors = etymologyExtractorRegistry;

        _formatter = formatter;
        _grammarText = grammarText;
        _languageDetection = languageDetectionService;
        _nonEnglishStorage = nonEnglishTextStorage;
        _ocrNormalizer = ocrNormalizer;
        _definitionNormalizer = definitionNormalizer;

        _batchCollector = batchCollector;
        _batchSettings = batchSettings?.Value ?? new();
        _useBatch = _batchCollector != null && _batchSettings.BatchSize > 1;
    }

    public async Task ExecuteAsync(string sourceCode, CancellationToken ct)
    {
        sourceCode = Helper.ParsingPipeline.NormalizeSourceCode(sourceCode);

        if (_useBatch)
            await ProcessBatchAsync(sourceCode, ct);
        else
            await ProcessSequentialAsync(sourceCode, ct);
    }

    // =========================
    // SEQUENTIAL (UNCHANGED)
    // =========================
    private async Task ProcessSequentialAsync(string sourceCode, CancellationToken ct)
    {
        var parser = _parserResolver.Resolve(sourceCode);
        var exampleExtractor = _exampleExtractors.GetExtractor(sourceCode);
        var synonymExtractor = _synonymExtractors.GetExtractor(sourceCode);
        var etymologyExtractor = _etymologyExtractors.GetExtractor(sourceCode);

        await foreach (var entry in StreamEntriesAsync(sourceCode, ct))
        {
            foreach (var parsed in ParseEntry(entry, parser, sourceCode))
            {
                var parsedId = await _parsedWriter.WriteAsync(
                    entry.DictionaryEntryId, parsed, sourceCode, ct);

                if (parsedId <= 0) continue;

                await WriteExamples(parsedId, parsed, exampleExtractor, sourceCode, ct);
                await WriteSynonyms(parsedId, entry, parsed, synonymExtractor, sourceCode, ct);
                await WriteEtymology(entry.DictionaryEntryId, entry, parsed, etymologyExtractor, sourceCode, ct);
                await WriteCrossReferences(parsedId, parsed, sourceCode, ct);
                await WriteAlias(parsedId, entry, parsed, sourceCode, ct);
            }
        }
    }

    // =========================
    // BATCH (OPTIMIZED)
    // =========================
    private async Task ProcessBatchAsync(string sourceCode, CancellationToken ct)
    {
        var parser = _parserResolver.Resolve(sourceCode);
        var exampleExtractor = _exampleExtractors.GetExtractor(sourceCode);
        var synonymExtractor = _synonymExtractors.GetExtractor(sourceCode);
        var etymologyExtractor = _etymologyExtractors.GetExtractor(sourceCode);

        await foreach (var entry in StreamEntriesAsync(sourceCode, ct))
        {
            ct.ThrowIfCancellationRequested();

            var parsedDefs = ParseEntry(entry, parser, sourceCode);

            await Parallel.ForEachAsync(
                parsedDefs,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = ct
                },
                async (parsed, token) =>
                {
                    await _batchCollector.AddParsedDefinitionAsync(
                        entry.DictionaryEntryId, parsed, sourceCode);

                    // Examples
                    foreach (var ex in exampleExtractor
                        .Extract(parsed)
                        .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var processed = await ProcessText(ex, "Example", sourceCode, token);
                        if (!Helper.ParsingPipeline.IsNonEnglishOrBilingualPlaceholder(processed.ProcessedText))
                            await _batchCollector.AddExampleAsync(0, processed.ProcessedText, sourceCode);
                    }

                    // Synonyms
                    var validSynonyms = new List<string>();

                    foreach (var r in synonymExtractor.Extract(
                        entry.Word, parsed.Definition, parsed.RawFragment))
                    {
                        if (r.ConfidenceLevel is not ("high" or "medium")) continue;
                        if (!synonymExtractor.ValidateSynonymPair(entry.Word, r.TargetHeadword)) continue;

                        var processed = await ProcessText(r.TargetHeadword, "Synonym", sourceCode, token);
                        if (Helper.ParsingPipeline.IsNonEnglishOrBilingualPlaceholder(processed.ProcessedText))
                            continue;

                        var clean = Helper.ParsingPipeline.NormalizeForSynonymDedupe(processed.ProcessedText);
                        if (!string.IsNullOrWhiteSpace(clean))
                            validSynonyms.Add(clean);
                    }

                    if (validSynonyms.Count > 0)
                        await _batchCollector.AddSynonymsAsync(0, validSynonyms, sourceCode);

                    // Cross references
                    if (parsed.CrossReferences != null)
                    {
                        foreach (var cr in parsed.CrossReferences)
                        {
                            if (!string.IsNullOrWhiteSpace(cr.TargetWord) &&
                                !Helper.ParsingPipeline.IsNonEnglishOrBilingualPlaceholder(cr.TargetWord))
                            {
                                await _batchCollector.AddCrossReferenceAsync(
                                    0,
                                    new CrossReference
                                    {
                                        TargetWord = cr.TargetWord,
                                        ReferenceType = cr.ReferenceType ?? "see"
                                    },
                                    sourceCode);
                            }
                        }
                    }

                    // Alias
                    if (!string.IsNullOrWhiteSpace(parsed.Alias))
                    {
                        var processed = await ProcessText(parsed.Alias, "Alias", sourceCode, token);
                        if (!Helper.ParsingPipeline.IsNonEnglishOrBilingualPlaceholder(processed.ProcessedText))
                        {
                            await _batchCollector.AddAliasAsync(
                                0,
                                processed.ProcessedText,
                                entry.DictionaryEntryId,
                                sourceCode);
                        }
                    }

                    // Etymology
                    var ety = etymologyExtractor.Extract(
                        entry.Word, parsed.Definition, parsed.RawFragment);

                    if (!string.IsNullOrWhiteSpace(ety.EtymologyText))
                    {
                        var processed = await ProcessText(ety.EtymologyText, "Etymology", sourceCode, token);

                        if (!Helper.ParsingPipeline.IsNonEnglishOrBilingualPlaceholder(processed.ProcessedText))
                        {
                            await _batchCollector.AddEtymologyAsync(new DictionaryEntryEtymology
                            {
                                DictionaryEntryId = entry.DictionaryEntryId,
                                EtymologyText = processed.ProcessedText,
                                LanguageCode = ety.LanguageCode,
                                SourceCode = sourceCode,
                                HasNonEnglishText = processed.NonEnglishTextId.HasValue,
                                NonEnglishTextId = processed.NonEnglishTextId,
                                CreatedUtc = DateTime.UtcNow
                            });
                        }
                    }
                });
        }

        await _batchCollector.FlushBatchAsync(ct);
    }

    // =========================
    // STREAM DB
    // =========================
    private async IAsyncEnumerable<DictionaryEntry> StreamEntriesAsync(
        string sourceCode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var reader = await conn.ExecuteReaderAsync(
            """
            SELECT DictionaryEntryId, Word, Definition, RawFragment, SenseNumber, SourceCode
            FROM dbo.DictionaryEntry
            WHERE SourceCode = @SourceCode
            ORDER BY DictionaryEntryId
            """,
            new { SourceCode = sourceCode });

        var parser = reader.GetRowParser<DictionaryEntry>();

        while (await reader.ReadAsync(ct))
            yield return parser(reader);
    }

    // =========================
    // ORIGINAL PRIVATE HELPERS (RESTORED)
    // =========================
    private async Task WriteExamples(
        long parsedId,
        ParsedDefinition parsed,
        IExampleExtractor extractor,
        string sourceCode,
        CancellationToken ct)
    {
        foreach (var ex in extractor.Extract(parsed).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var processed = await ProcessText(ex, "Example", sourceCode, ct);
            if (!Helper.ParsingPipeline.IsNonEnglishOrBilingualPlaceholder(processed.ProcessedText))
                await _exampleWriter.WriteAsync(parsedId, processed.ProcessedText, sourceCode, ct);
        }
    }

    private async Task WriteSynonyms(
        long parsedId,
        DictionaryEntry entry,
        ParsedDefinition parsed,
        ISynonymExtractor extractor,
        string sourceCode,
        CancellationToken ct)
    {
        var valid = new List<string>();

        foreach (var r in extractor.Extract(entry.Word, parsed.Definition, parsed.RawFragment))
        {
            if (r.ConfidenceLevel is not ("high" or "medium")) continue;
            if (!extractor.ValidateSynonymPair(entry.Word, r.TargetHeadword)) continue;

            var processed = await ProcessText(r.TargetHeadword, "Synonym", sourceCode, ct);
            var clean = Helper.ParsingPipeline.NormalizeForSynonymDedupe(processed.ProcessedText);

            if (!string.IsNullOrWhiteSpace(clean))
                valid.Add(clean);
        }

        if (valid.Count > 0)
            await _synonymWriter.WriteSynonymsForParsedDefinition(parsedId, valid, sourceCode, ct);
    }

    private async Task WriteEtymology(
        long dictionaryEntryId,
        DictionaryEntry entry,
        ParsedDefinition parsed,
        IEtymologyExtractor extractor,
        string sourceCode,
        CancellationToken ct)
    {
        var ety = extractor.Extract(entry.Word, parsed.Definition, parsed.RawFragment);
        if (string.IsNullOrWhiteSpace(ety.EtymologyText)) return;

        var processed = await ProcessText(ety.EtymologyText, "Etymology", sourceCode, ct);

        await _etymologyWriter.WriteAsync(new DictionaryEntryEtymology
        {
            DictionaryEntryId = dictionaryEntryId,
            EtymologyText = processed.ProcessedText,
            LanguageCode = ety.LanguageCode,
            SourceCode = sourceCode,
            HasNonEnglishText = processed.NonEnglishTextId.HasValue,
            NonEnglishTextId = processed.NonEnglishTextId,
            CreatedUtc = DateTime.UtcNow
        }, ct);
    }

    private async Task WriteCrossReferences(
        long parsedId,
        ParsedDefinition parsed,
        string sourceCode,
        CancellationToken ct)
    {
        if (parsed.CrossReferences == null) return;

        foreach (var cr in parsed.CrossReferences)
            await _crossRefWriter.WriteAsync(parsedId, cr, sourceCode, ct);
    }

    private async Task WriteAlias(
        long parsedId,
        DictionaryEntry entry,
        ParsedDefinition parsed,
        string sourceCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parsed.Alias)) return;

        var processed = await ProcessText(parsed.Alias, "Alias", sourceCode, ct);
        await _aliasWriter.WriteAsync(parsedId, processed.ProcessedText, sourceCode, ct);

        if (!string.Equals(entry.Word, parsed.Alias, StringComparison.OrdinalIgnoreCase))
        {
            await _variantWriter.WriteAsync(
                entry.DictionaryEntryId,
                parsed.Alias.Trim(),
                "alias",
                sourceCode,
                ct);
        }
    }

    // =========================
    // COMMON
    // =========================
    private IEnumerable<ParsedDefinition> ParseEntry(
        DictionaryEntry entry,
        IDictionaryDefinitionParser parser,
        string sourceCode)
        => parser.Parse(entry) ?? new[] { CreateFallback(entry, sourceCode) };

    private async Task<(string ProcessedText, long? NonEnglishTextId)> ProcessText(
        string text,
        string fieldType,
        string sourceCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, null);

        text = _ocrNormalizer.Normalize(text, "en");

        if (fieldType == "Definition")
            text = _definitionNormalizer.Normalize(text);

        if (!_languageDetection.ContainsNonEnglish(text))
        {
            var formatted = fieldType switch
            {
                "Example" => _formatter.FormatExample(text),
                "Synonym" or "Alias" => _formatter.FormatSynonym(text),
                "Etymology" => _formatter.FormatEtymology(text),
                _ => _formatter.FormatDefinition(text)
            };

            return (formatted, null);
        }

        var id = await _nonEnglishStorage.StoreNonEnglishTextAsync(
            text, sourceCode, fieldType, ct);

        return (Helper.ParsingPipeline.BuildPlaceholder(fieldType, false), id);
    }

    private static ParsedDefinition CreateFallback(DictionaryEntry entry, string sourceCode)
        => new()
        {
            MeaningTitle = entry.Word,
            Definition = entry.Definition ?? string.Empty,
            RawFragment = entry.RawFragment ?? entry.Definition ?? string.Empty,
            SenseNumber = entry.SenseNumber,
            SourceCode = sourceCode,
            CrossReferences = new List<CrossReference>()
        };
}