// Update DictionaryParsedDefinitionProcessor.cs

namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class DictionaryParsedDefinitionProcessor
{
    private readonly SqlDictionaryAliasWriter _aliasWriter;
    private readonly string _connectionString;
    private readonly SqlDictionaryEntryCrossReferenceWriter _crossRefWriter;
    private readonly IEtymologyExtractorRegistry _etymologyExtractorRegistry;
    private readonly IEntryEtymologyWriter _etymologyWriter;
    private readonly IExampleExtractorRegistry _exampleExtractorRegistry; // NEW
    private readonly IDictionaryEntryExampleWriter _exampleWriter;
    private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;
    private readonly SqlParsedDefinitionWriter _parsedWriter;
    private readonly IDictionaryDefinitionParser _parser;
    private readonly ISynonymExtractorRegistry _synonymExtractorRegistry;
    private readonly IDictionaryEntrySynonymWriter _synonymWriter;
    private readonly SqlDictionaryEntryVariantWriter _variantWriter;

    public DictionaryParsedDefinitionProcessor(
        string connectionString,
        IDictionaryDefinitionParser parser,
        SqlParsedDefinitionWriter parsedWriter,
        SqlDictionaryEntryCrossReferenceWriter crossRefWriter,
        SqlDictionaryAliasWriter aliasWriter,
        IEntryEtymologyWriter etymologyWriter,
        SqlDictionaryEntryVariantWriter variantWriter,
        IDictionaryEntryExampleWriter exampleWriter,
        IExampleExtractorRegistry exampleExtractorRegistry,
        ISynonymExtractorRegistry synonymExtractorRegistry,
        IDictionaryEntrySynonymWriter synonymWriter,
        IEtymologyExtractorRegistry etymologyExtractorRegistry, // NEW
        ILogger<DictionaryParsedDefinitionProcessor> logger)
    {
        _connectionString = connectionString;
        _parser = parser;
        _parsedWriter = parsedWriter;
        _crossRefWriter = crossRefWriter;
        _aliasWriter = aliasWriter;
        _etymologyWriter = etymologyWriter;
        _variantWriter = variantWriter;
        _exampleWriter = exampleWriter;
        _exampleExtractorRegistry = exampleExtractorRegistry;
        _synonymExtractorRegistry = synonymExtractorRegistry;
        _synonymWriter = synonymWriter; // Store it
        _etymologyExtractorRegistry = etymologyExtractorRegistry;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        string sourceCode,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Stage=Parsing started | Source={SourceCode}",
            sourceCode);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // --------------------------------------------------
        // 1. Load ALL DictionaryEntry rows for source
        // --------------------------------------------------
        var entries = (await conn.QueryAsync<DictionaryEntry>(
                """
                SELECT *
                FROM dbo.DictionaryEntry
                WHERE SourceCode = @SourceCode
                """,
                new { SourceCode = sourceCode }))
            .ToList();

        _logger.LogInformation(
            "Stage=Parsing | EntriesLoaded={Count} | Source={SourceCode}",
            entries.Count,
            sourceCode);

        // Get the appropriate extractor for this source
        var exampleExtractor = _exampleExtractorRegistry.GetExtractor(sourceCode);
        _logger.LogDebug(
            "Using example extractor: {ExtractorType} for source {Source}",
            exampleExtractor.GetType().Name,
            sourceCode);

        var entryIndex = 0;
        var parsedInserted = 0;
        var crossRefInserted = 0;
        var aliasInserted = 0;
        var variantInserted = 0;
        var exampleInserted = 0;
        var synonymInserted = 0;
        var etymologyExtracted = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            entryIndex++;

            if (entryIndex % 1_000 == 0)
                _logger.LogInformation(
                    "Stage=Parsing completed | Source={SourceCode} | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants} | Examples={Examples} | Synonyms={Synonyms}",
                    sourceCode,
                    entryIndex,
                    parsedInserted,
                    crossRefInserted,
                    aliasInserted,
                    variantInserted,
                    exampleInserted,
                    synonymInserted);

            // --------------------------------------------------
            // 2. Parse entry
            // --------------------------------------------------
            var parsedDefinitions = _parser.Parse(entry)?.ToList();

            if (parsedDefinitions == null || parsedDefinitions.Count == 0)
                parsedDefinitions = new List<ParsedDefinition>
                {
                    new()
                    {
                        Definition = null,
                        RawFragment = entry.Definition,
                        SenseNumber = entry.SenseNumber
                    }
                };

            if (parsedDefinitions.Count != 1)
                throw new InvalidOperationException(
                    $"Parser returned {parsedDefinitions.Count} ParsedDefinitions for DictionaryEntryId={entry.DictionaryEntryId}. Exactly 1 is required.");

            foreach (var parsed in parsedDefinitions)
            {
                // Inside the foreach loop for parsedDefinitions
                var currentParsed = parsed;
                // --------------------------------------------------
                // 3. Persist parsed definition
                // --------------------------------------------------
                var parsedId = await _parsedWriter.WriteAsync(
                    entry.DictionaryEntryId,
                    currentParsed,
                    null,
                    ct);

                if (parsedId <= 0)
                    throw new InvalidOperationException(
                        $"ParsedDefinition insert failed for DictionaryEntryId={entry.DictionaryEntryId}");

                parsedInserted++;

                // --------------------------------------------------
                // 4. Extract and save examples (GENERIC APPROACH)
                // --------------------------------------------------
                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var examples = exampleExtractor.Extract(currentParsed);

                    foreach (var example in examples)
                    {
                        await _exampleWriter.WriteAsync(
                            parsedId,
                            example,
                            sourceCode,
                            ct);

                        exampleInserted++;
                    }

                    if (examples.Count > 0)
                        _logger.LogDebug(
                            "Extracted {Count} examples for parsed definition {ParsedId}",
                            examples.Count,
                            parsedId);
                }

                // --------------------------------------------------
                // 5. Extract and save synonyms
                // --------------------------------------------------
                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var synonymExtractor = _synonymExtractorRegistry.GetExtractor(sourceCode);
                    var synonymResults = synonymExtractor.Extract(
                        entry.Word,
                        currentParsed.Definition,
                        currentParsed.RawFragment);

                    var validSynonyms = new List<string>();

                    foreach (var synonymResult in synonymResults)
                        // Only process high and medium confidence synonyms
                        if (synonymResult.ConfidenceLevel == "high" || synonymResult.ConfidenceLevel == "medium")
                            if (synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword))
                            {
                                validSynonyms.Add(synonymResult.TargetHeadword);

                                _logger.LogDebug(
                                    "Synonym detected | Headword={Headword} | Synonym={Synonym} | Confidence={Confidence}",
                                    entry.Word,
                                    synonymResult.TargetHeadword,
                                    synonymResult.ConfidenceLevel);
                            }

                    // Save valid synonyms
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

                // --------------------------------------------------
                // 6. Extract and save etymology
                // --------------------------------------------------
                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var etymologyExtractor = _etymologyExtractorRegistry.GetExtractor(sourceCode);
                    var etymologyResult = etymologyExtractor.Extract(
                        entry.Word,
                        currentParsed.Definition,
                        currentParsed.RawFragment);

                    // If etymology was found in definition, save it
                    if (!string.IsNullOrWhiteSpace(etymologyResult.EtymologyText))
                    {
                        await _etymologyWriter.WriteAsync(
                            new DictionaryEntryEtymology
                            {
                                DictionaryEntryId = entry.DictionaryEntryId,
                                EtymologyText = etymologyResult.EtymologyText,
                                LanguageCode = etymologyResult.LanguageCode,
                                CreatedUtc = DateTime.UtcNow
                            },
                            ct);

                        etymologyExtracted++;

                        _logger.LogDebug(
                            "Etymology extracted from definition | Headword={Headword} | Etymology={Etymology} | Method={Method}",
                            entry.Word,
                            etymologyResult.EtymologyText.Substring(0,
                                Math.Min(100, etymologyResult.EtymologyText.Length)),
                            etymologyResult.DetectionMethod);

                        // Update workingParsed to use cleaned definition (without etymology)
                        if (!string.IsNullOrWhiteSpace(etymologyResult.CleanedDefinition))
                            currentParsed = new ParsedDefinition
                            {
                                ParentKey = currentParsed.ParentKey,
                                SelfKey = currentParsed.SelfKey,
                                MeaningTitle = currentParsed.MeaningTitle,
                                SenseNumber = currentParsed.SenseNumber,
                                Definition = etymologyResult.CleanedDefinition,
                                RawFragment = currentParsed.RawFragment,
                                Domain = currentParsed.Domain,
                                UsageLabel = currentParsed.UsageLabel,
                                Alias = currentParsed.Alias,
                                Synonyms = currentParsed.Synonyms,
                                CrossReferences = currentParsed.CrossReferences
                            };
                    }
                }

                // --------------------------------------------------
                // 7. Cross-references
                // --------------------------------------------------
                foreach (var cr in currentParsed.CrossReferences)
                {
                    await _crossRefWriter.WriteAsync(
                        parsedId,
                        cr,
                        ct);

                    crossRefInserted++;
                }

                // --------------------------------------------------
                // 8. Alias
                // --------------------------------------------------
                if (!string.IsNullOrWhiteSpace(currentParsed.Alias)) // Use workingParsed
                {
                    await _aliasWriter.WriteAsync(
                        parsedId,
                        currentParsed.Alias, // Use workingParsed
                        ct);

                    aliasInserted++;
                }
            }

            _logger.LogInformation(
                "Stage=Parsing completed | Source={SourceCode} | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants} | Examples={Examples}",
                sourceCode,
                entryIndex,
                parsedInserted,
                crossRefInserted,
                aliasInserted,
                variantInserted,
                exampleInserted);
        }
    }
}