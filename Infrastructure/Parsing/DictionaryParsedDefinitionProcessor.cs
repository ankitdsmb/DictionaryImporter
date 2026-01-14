namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class DictionaryParsedDefinitionProcessor(
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
    IEtymologyExtractorRegistry etymologyExtractorRegistry,
    ILogger<DictionaryParsedDefinitionProcessor> logger)
{
    private readonly SqlDictionaryEntryVariantWriter _variantWriter = variantWriter;

    public async Task ExecuteAsync(
        string sourceCode,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Stage=Parsing started | Source={SourceCode}",
            sourceCode);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var entries = (await conn.QueryAsync<DictionaryEntry>(
                """
                SELECT *
                FROM dbo.DictionaryEntry
                WHERE SourceCode = @SourceCode
                """,
                new { SourceCode = sourceCode }))
            .ToList();

        logger.LogInformation(
            "Stage=Parsing | EntriesLoaded={Count} | Source={SourceCode}",
            entries.Count,
            sourceCode);

        var exampleExtractor = exampleExtractorRegistry.GetExtractor(sourceCode);
        logger.LogDebug(
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
                logger.LogInformation(
                    "Stage=Parsing completed | Source={SourceCode} | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants} | Examples={Examples} | Synonyms={Synonyms}",
                    sourceCode,
                    entryIndex,
                    parsedInserted,
                    crossRefInserted,
                    aliasInserted,
                    variantInserted,
                    exampleInserted,
                    synonymInserted);

            var parsedDefinitions = parser.Parse(entry)?.ToList();

            if (parsedDefinitions == null || parsedDefinitions.Count == 0)
                parsedDefinitions =
                [
                    new()
                    {
                        Definition = null,
                        RawFragment = entry.Definition,
                        SenseNumber = entry.SenseNumber
                    }
                ];

            if (parsedDefinitions.Count != 1)
                throw new InvalidOperationException(
                    $"Parser returned {parsedDefinitions.Count} ParsedDefinitions for DictionaryEntryId={entry.DictionaryEntryId}. Exactly 1 is required.");

            foreach (var parsed in parsedDefinitions)
            {
                var currentParsed = parsed;
                var parsedId = await parsedWriter.WriteAsync(
                    entry.DictionaryEntryId,
                    currentParsed,
                    null,
                    ct);

                if (parsedId <= 0)
                    throw new InvalidOperationException(
                        $"ParsedDefinition insert failed for DictionaryEntryId={entry.DictionaryEntryId}");

                parsedInserted++;

                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var examples = exampleExtractor.Extract(currentParsed);

                    foreach (var example in examples)
                    {
                        await exampleWriter.WriteAsync(
                            parsedId,
                            example,
                            sourceCode,
                            ct);

                        exampleInserted++;
                    }

                    if (examples.Count > 0)
                        logger.LogDebug(
                            "Extracted {Count} examples for parsed definition {ParsedId}",
                            examples.Count,
                            parsedId);
                }

                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var synonymExtractor = synonymExtractorRegistry.GetExtractor(sourceCode);
                    var synonymResults = synonymExtractor.Extract(
                        entry.Word,
                        currentParsed.Definition,
                        currentParsed.RawFragment);

                    var validSynonyms = new List<string>();

                    foreach (var synonymResult in synonymResults)
                        if (synonymResult.ConfidenceLevel == "high" || synonymResult.ConfidenceLevel == "medium")
                            if (synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword))
                            {
                                validSynonyms.Add(synonymResult.TargetHeadword);

                                logger.LogDebug(
                                    "Synonym detected | Headword={Headword} | Synonym={Synonym} | Confidence={Confidence}",
                                    entry.Word,
                                    synonymResult.TargetHeadword,
                                    synonymResult.ConfidenceLevel);
                            }

                    if (validSynonyms.Count > 0)
                    {
                        await synonymWriter.WriteSynonymsForParsedDefinition(
                            parsedId,
                            validSynonyms,
                            sourceCode,
                            ct);

                        synonymInserted += validSynonyms.Count;
                    }
                }

                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var etymologyExtractor = etymologyExtractorRegistry.GetExtractor(sourceCode);
                    var etymologyResult = etymologyExtractor.Extract(
                        entry.Word,
                        currentParsed.Definition,
                        currentParsed.RawFragment);

                    if (!string.IsNullOrWhiteSpace(etymologyResult.EtymologyText))
                    {
                        await etymologyWriter.WriteAsync(
                            new DictionaryEntryEtymology
                            {
                                DictionaryEntryId = entry.DictionaryEntryId,
                                EtymologyText = etymologyResult.EtymologyText,
                                LanguageCode = etymologyResult.LanguageCode,
                                CreatedUtc = DateTime.UtcNow
                            },
                            ct);

                        etymologyExtracted++;

                        logger.LogDebug(
                            "Etymology extracted from definition | Headword={Headword} | Etymology={Etymology} | Method={Method}",
                            entry.Word,
                            etymologyResult.EtymologyText.Substring(0,
                                Math.Min(100, etymologyResult.EtymologyText.Length)),
                            etymologyResult.DetectionMethod);

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

                foreach (var cr in currentParsed.CrossReferences)
                {
                    await crossRefWriter.WriteAsync(
                        parsedId,
                        cr,
                        ct);

                    crossRefInserted++;
                }

                if (!string.IsNullOrWhiteSpace(currentParsed.Alias))
                {
                    await aliasWriter.WriteAsync(
                        parsedId,
                        currentParsed.Alias, ct);

                    aliasInserted++;
                }
            }

            logger.LogInformation(
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