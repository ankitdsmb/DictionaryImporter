using Dapper;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using DictionaryImporter.Sources.EnglishChinese;
using DictionaryImporter.Sources.EnglishChinese.Parsing;
using HtmlAgilityPack;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class DictionaryParsedDefinitionProcessor : IParsedDefinitionProcessor
{
    private readonly string _connectionString;
    private readonly IDictionaryDefinitionParserResolver _parserResolver;
    private readonly SqlParsedDefinitionWriter _parsedWriter;
    private readonly IDictionaryEntryCrossReferenceWriter _crossRefWriter;
    private readonly IDictionaryEntryAliasWriter _aliasWriter;
    private readonly IEntryEtymologyWriter _etymologyWriter;
    private readonly IDictionaryEntryVariantWriter _variantWriter;
    private readonly IDictionaryEntryExampleWriter _exampleWriter;
    private readonly IExampleExtractorRegistry _exampleExtractorRegistry;
    private readonly ISynonymExtractorRegistry _synonymExtractorRegistry;
    private readonly IDictionaryEntrySynonymWriter _synonymWriter;
    private readonly IEtymologyExtractorRegistry _etymologyExtractorRegistry;
    private readonly IDictionaryTextFormatter _formatter;
    private readonly IGrammarEnrichedTextService _grammarText;
    private readonly ILanguageDetectionService _languageDetectionService;
    private readonly INonEnglishTextStorage _nonEnglishTextStorage;
    private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;

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
        ILogger<DictionaryParsedDefinitionProcessor> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _parserResolver = parserResolver ?? throw new ArgumentNullException(nameof(parserResolver));
        _parsedWriter = parsedWriter ?? throw new ArgumentNullException(nameof(parsedWriter));
        _crossRefWriter = crossRefWriter ?? throw new ArgumentNullException(nameof(crossRefWriter));
        _aliasWriter = aliasWriter ?? throw new ArgumentNullException(nameof(aliasWriter));
        _etymologyWriter = etymologyWriter ?? throw new ArgumentNullException(nameof(etymologyWriter));
        _variantWriter = variantWriter ?? throw new ArgumentNullException(nameof(variantWriter));
        _exampleWriter = exampleWriter ?? throw new ArgumentNullException(nameof(exampleWriter));
        _exampleExtractorRegistry = exampleExtractorRegistry ?? throw new ArgumentNullException(nameof(exampleExtractorRegistry));
        _synonymExtractorRegistry = synonymExtractorRegistry ?? throw new ArgumentNullException(nameof(synonymExtractorRegistry));
        _synonymWriter = synonymWriter ?? throw new ArgumentNullException(nameof(synonymWriter));
        _etymologyExtractorRegistry = etymologyExtractorRegistry ?? throw new ArgumentNullException(nameof(etymologyExtractorRegistry));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _grammarText = grammarText ?? throw new ArgumentNullException(nameof(grammarText));
        _languageDetectionService = languageDetectionService ?? throw new ArgumentNullException(nameof(languageDetectionService));
        _nonEnglishTextStorage = nonEnglishTextStorage ?? throw new ArgumentNullException(nameof(nonEnglishTextStorage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(string sourceCode, CancellationToken ct)
    {
        //if (!SourceDataHelper.ShouldContinueProcessing(sourceCode, _logger))
        //{
        //    _logger.LogInformation("Source {SourceCode} processing limit reached, skipping", sourceCode);
        //    return;
        //}

        _logger.LogInformation("Stage=Parsing started | Source={SourceCode}", sourceCode);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var entries = (await conn.QueryAsync<DictionaryEntry>(
            """
            SELECT DictionaryEntryId, Word, Definition, RawFragment, SenseNumber, SourceCode
            FROM dbo.DictionaryEntry
            WHERE SourceCode = @SourceCode
            """,
            new { SourceCode = sourceCode }))
            .ToList();

        _logger.LogInformation(
            "Processing {Count} entries for source {SourceCode}",
            entries.Count, sourceCode);

        var entryIndex = 0;
        var parsedInserted = 0;
        var crossRefInserted = 0;
        var aliasInserted = 0;
        var exampleInserted = 0;
        var synonymInserted = 0;
        var etymologyExtracted = 0;
        var nonEnglishEntries = 0;
        var nonEnglishExamples = 0;
        var nonEnglishSynonyms = 0;
        var nonEnglishEtymology = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            entryIndex++;

            if (entryIndex % 1000 == 0)
            {
                _logger.LogInformation(
                    "Parsing progress | Source={SourceCode} | Processed={Processed}/{Total}",
                    sourceCode, entryIndex, entries.Count);
            }

            var parser = _parserResolver.Resolve(sourceCode);
            var parsedDefinitions = parser.Parse(entry)?.ToList();

            if (parsedDefinitions == null || parsedDefinitions.Count == 0)
            {
                _logger.LogWarning(
                    "Parser returned empty for entry {Word} ({Source})",
                    entry.Word, sourceCode);

                parsedDefinitions = new List<ParsedDefinition>
                {
                    CreateFallbackDefinition(entry, sourceCode)
                };
            }

            foreach (var parsed in parsedDefinitions)
            {
                var (processedDefinition, definitionNonEnglishTextId) = await ProcessTextContent(
                    parsed.Definition,
                    "Definition",
                    sourceCode,
                    ct);

                var currentParsed = new ParsedDefinition
                {
                    ParentKey = parsed.ParentKey,
                    SelfKey = parsed.SelfKey,
                    MeaningTitle = parsed.MeaningTitle,
                    SenseNumber = parsed.SenseNumber,
                    Definition = processedDefinition,
                    RawFragment = parsed.RawFragment,
                    // In the ExecuteAsync method, when creating currentParsed:
                    Domain = SourceDataHelper.ExtractProperDomain(sourceCode, parsed.Domain, parsed.Definition),
                    UsageLabel = parsed.UsageLabel,
                    Alias = parsed.Alias,
                    Synonyms = parsed.Synonyms,
                    CrossReferences = parsed.CrossReferences,
                    SourceCode = sourceCode,
                    HasNonEnglishText = definitionNonEnglishTextId.HasValue,
                    NonEnglishTextId = definitionNonEnglishTextId
                };

                // Write parsed definition
                var parsedId = await _parsedWriter.WriteAsync(
                    entry.DictionaryEntryId,
                    currentParsed,
                    sourceCode,
                    ct);

                if (parsedId <= 0)
                {
                    _logger.LogError(
                        "Failed to insert parsed definition for entry {EntryId}, Word={Word}",
                        entry.DictionaryEntryId, entry.Word);
                    continue;
                }

                parsedInserted++;
                if (definitionNonEnglishTextId.HasValue) nonEnglishEntries++;

                // Process and write examples
                if (!string.IsNullOrWhiteSpace(parsed.Definition) || parsed.Examples?.Count > 0)
                {
                    var exampleExtractor = _exampleExtractorRegistry.GetExtractor(sourceCode);
                    var examples = exampleExtractor.Extract(currentParsed);

                    foreach (var exampleText in examples)
                    {
                        ct.ThrowIfCancellationRequested();

                        var (processedExample, exampleNonEnglishTextId) = await ProcessTextContent(
                            exampleText,
                            "Example",
                            sourceCode,
                            ct);

                        var formattedExample = _formatter.FormatExample(processedExample);
                        var correctedExample = await _grammarText.NormalizeExampleAsync(formattedExample, ct);

                        await _exampleWriter.WriteAsync(
                            parsedId,
                            correctedExample,
                            sourceCode,
                            ct);

                        exampleInserted++;
                        if (exampleNonEnglishTextId.HasValue) nonEnglishExamples++;
                    }
                }

                // Process and write synonyms
                if (!string.IsNullOrWhiteSpace(parsed.Definition))
                {
                    var synonymExtractor = _synonymExtractorRegistry.GetExtractor(sourceCode);
                    var synonymResults = synonymExtractor.Extract(
                        entry.Word,
                        parsed.Definition,
                        parsed.RawFragment);

                    var validSynonyms = new List<string>();
                    foreach (var synonymResult in synonymResults)
                    {
                        if (synonymResult.ConfidenceLevel is not ("high" or "medium"))
                            continue;

                        if (!synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword))
                            continue;

                        var (processedSynonym, synonymNonEnglishTextId) = await ProcessTextContent(
                            synonymResult.TargetHeadword,
                            "Synonym",
                            sourceCode,
                            ct);

                        var cleanedSynonym = _formatter.FormatSynonym(processedSynonym);
                        if (!string.IsNullOrWhiteSpace(cleanedSynonym))
                            validSynonyms.Add(cleanedSynonym);
                    }

                    if (validSynonyms.Count > 0)
                    {
                        await _synonymWriter.WriteSynonymsForParsedDefinition(
                            parsedId,
                            validSynonyms,
                            sourceCode,
                            ct);

                        synonymInserted += validSynonyms.Count;
                    }
                }

                // Process and write etymology
                if (!string.IsNullOrWhiteSpace(parsed.Definition))
                {
                    var etymologyExtractor = _etymologyExtractorRegistry.GetExtractor(sourceCode);
                    var etymologyResult = etymologyExtractor.Extract(
                        entry.Word,
                        parsed.Definition,
                        parsed.RawFragment);

                    if (!string.IsNullOrWhiteSpace(etymologyResult.EtymologyText))
                    {
                        var (processedEtymology, etymologyNonEnglishTextId) = await ProcessTextContent(
                            etymologyResult.EtymologyText,
                            "Etymology",
                            sourceCode,
                            ct);

                        await _etymologyWriter.WriteAsync(
                            new DictionaryEntryEtymology
                            {
                                DictionaryEntryId = entry.DictionaryEntryId,
                                EtymologyText = processedEtymology,
                                LanguageCode = etymologyResult.LanguageCode,
                                SourceCode = sourceCode,
                                HasNonEnglishText = etymologyNonEnglishTextId.HasValue,
                                NonEnglishTextId = etymologyNonEnglishTextId,
                                CreatedUtc = DateTime.UtcNow
                            },
                            ct);

                        etymologyExtracted++;
                        if (etymologyNonEnglishTextId.HasValue) nonEnglishEtymology++;
                    }
                }

                // Write cross references
                if (currentParsed.CrossReferences != null)
                {
                    foreach (var cr in currentParsed.CrossReferences)
                    {
                        await _crossRefWriter.WriteAsync(parsedId, cr, sourceCode, ct);
                        crossRefInserted++;
                    }
                }

                // Write alias
                if (!string.IsNullOrWhiteSpace(currentParsed.Alias))
                {
                    var (processedAlias, aliasNonEnglishTextId) = await ProcessTextContent(
                        currentParsed.Alias,
                        "Alias",
                        sourceCode,
                        ct);

                    await _aliasWriter.WriteAsync(
                        parsedId,
                        processedAlias,
                        ct);

                    aliasInserted++;
                }

                // NOTE: Variant writing is not implemented yet
                // The variant writer exists but we don't have variant data to pass
                // If you need variants, you'll need to:
                // 1. Extract variant data from parsed definitions
                // 2. Call _variantWriter.WriteAsync with the correct parameters
                // 3. Implement variant detection logic
            }
        }

        _logger.LogInformation(
            "Stage=Parsing completed | Source={SourceCode} | " +
            "Entries={Entries} | Parsed={Parsed} | CrossRefs={CrossRefs} | " +
            "Aliases={Aliases} | Examples={Examples} | " +
            "Synonyms={Synonyms} | Etymology={Etymology} | " +
            "NonEnglish: Entries={NonEnglishEntries}, Examples={NonEnglishExamples}, Etymology={NonEnglishEtymology}",
            sourceCode,
            entries.Count,
            parsedInserted,
            crossRefInserted,
            aliasInserted,
            exampleInserted,
            synonymInserted,
            etymologyExtracted,
            nonEnglishEntries,
            nonEnglishExamples,
            nonEnglishEtymology);
    }

    private async Task<(string ProcessedText, long? NonEnglishTextId)> ProcessTextContent(
        string text,
        string fieldType,
        string sourceCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (string.Empty, null);

        // ✅ DETECT BILINGUAL TEXT (CRITICAL FOR ENG_CHN)
        var isBilingual = _languageDetectionService.IsBilingualText(text);
        var containsNonEnglish = _languageDetectionService.ContainsNonEnglish(text);

        if (!containsNonEnglish && !isBilingual)
        {
            // English-only text
            var formattedText = _formatter.FormatDefinition(text);
            var normalizedText = await _grammarText.NormalizeDefinitionAsync(formattedText, ct);
            return (normalizedText ?? formattedText, null);
        }

        // ✅ Non-English or mixed language text
        var nonEnglishTextId = await _nonEnglishTextStorage.StoreNonEnglishTextAsync(
            text,
            sourceCode,
            fieldType,
            ct);

        var placeholder = isBilingual
            ? $"[BILINGUAL_{fieldType.ToUpper()}]"
            : $"[NON_ENGLISH_{fieldType.ToUpper()}]";

        _logger.LogDebug(
            "Stored {Type} text | Field={Field} | Source={Source} | TextId={TextId} | Bilingual={Bilingual}",
            isBilingual ? "bilingual" : "non-English",
            fieldType, sourceCode, nonEnglishTextId, isBilingual);

        return (placeholder, nonEnglishTextId);
    }

    private ParsedDefinition CreateFallbackDefinition(DictionaryEntry entry, string sourceCode)
    {
        return new ParsedDefinition
        {
            MeaningTitle = entry.Word ?? "unnamed sense",
            Definition = entry.Definition ?? string.Empty,
            RawFragment = entry.RawFragment ?? entry.Definition ?? string.Empty,
            SenseNumber = entry.SenseNumber,
            CrossReferences = new List<CrossReference>(),
            SourceCode = sourceCode,
            HasNonEnglishText = false
        };
    }
    private string GetPreview(string text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) return "[empty]";
        if (text.Length <= length) return text;
        return text.Substring(0, length) + "...";
    }
}