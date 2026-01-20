using Dapper;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Sources.Common.Parsing;
using DictionaryImporter.Sources.EnglishChinese;
using DictionaryImporter.Sources.EnglishChinese.Parsing; // Add this for SimpleEngChnExtractor
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class DictionaryParsedDefinitionProcessor(
    string connectionString,
    IDictionaryDefinitionParserResolver parserResolver,
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
    IDictionaryTextFormatter formatter,
    IGrammarEnrichedTextService grammarText,
    ILogger<DictionaryParsedDefinitionProcessor> logger) : IParsedDefinitionProcessor
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
            "DEBUG: Database entries for {SourceCode}: Count={Count} | Sample words: {SampleWords}",
            sourceCode,
            entries.Count,
            string.Join(", ", entries.Take(5).Select(e => e.Word)));

        // ✅ SPECIAL HANDLING FOR ENG_CHN - Log that we're using enhanced extraction
        if (sourceCode == "ENG_CHN")
        {
            logger.LogInformation(
                "ENG_CHN special processing: Using SimpleEngChnExtractor for Chinese preservation");
        }

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
        var engChnFixedCount = 0; // Track how many ENG_CHN entries were specially processed

        foreach (var entry in entries)
        {
            logger.LogInformation(
                "PARSER DEBUG: Source={SourceCode} | Word={Word} | RawFragmentLength={Length}",
                sourceCode, entry.Word, entry.RawFragment?.Length ?? 0);

            ct.ThrowIfCancellationRequested();
            entryIndex++;

            if (entryIndex % 1_000 == 0)
            {
                logger.LogInformation(
                    "Stage=Parsing progress | Source={SourceCode} | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants} | Examples={Examples} | Synonyms={Synonyms} | ENG_CHN_Fixed={EngChnFixed}",
                    sourceCode,
                    entryIndex,
                    parsedInserted,
                    crossRefInserted,
                    aliasInserted,
                    variantInserted,
                    exampleInserted,
                    synonymInserted,
                    engChnFixedCount);
            }

            var parser = parserResolver.Resolve(sourceCode);
            logger.LogInformation(
                "PARSER DEBUG: Using {ParserType} for {SourceCode} | Word={Word}",
                parser.GetType().Name,
                sourceCode,
                entry.Word);

            // In DictionaryParsedDefinitionProcessor.ExecuteAsync, replace the fallback section:

            var parsedDefinitions = parser.Parse(entry)?.ToList();

            // ✅ ADD PROPER ERROR LOGGING BEFORE FALLBACK
            if (parsedDefinitions == null || parsedDefinitions.Count == 0)
            {
                logger.LogWarning(
                    "⚠️ PARSER RETURNED EMPTY | Source={Source} | Word={Word} | RawFragmentPreview={Preview} | ParserType={ParserType}",
                    sourceCode,
                    entry.Word,
                    GetPreview(entry.RawFragment, 100),
                    parser.GetType().Name);

                // ✅ SPECIAL CASE FOR ENG_CHN: Use SimpleEngChnExtractor directly if parser fails
                if (sourceCode == "ENG_CHN" && !string.IsNullOrWhiteSpace(entry.RawFragment))
                {
                    logger.LogWarning(
                        "ENG_CHN parser failed, using SimpleEngChnExtractor fallback for: {Word}",
                        entry.Word);

                    var extractedDefinition = SimpleEngChnExtractor.ExtractDefinition(entry.RawFragment);

                    parsedDefinitions =
                    [
                        new ParsedDefinition
                        {
                            Definition = extractedDefinition,
                            RawFragment = entry.RawFragment,
                            SenseNumber = entry.SenseNumber,
                            CrossReferences = new List<CrossReference>(),
                            MeaningTitle = entry.Word ?? "unnamed sense"
                        }
                    ];
                    engChnFixedCount++;
                }
                else
                {
                    parsedDefinitions =
                    [
                        new ParsedDefinition
                        {
                            Definition = null,
                            RawFragment = entry.Definition,
                            SenseNumber = entry.SenseNumber,
                            CrossReferences = new List<CrossReference>(),
                            MeaningTitle = entry.Word ?? "unnamed sense"
                        }
                    ];
                }
            }

            foreach (var parsed in parsedDefinitions)
            {
                var currentParsed = parsed;
                var inputDefinition =
                    !string.IsNullOrWhiteSpace(currentParsed.Definition)
                        ? currentParsed.Definition
                        : currentParsed.RawFragment;

                if (!string.IsNullOrWhiteSpace(inputDefinition))
                {
                    // ✅ SPECIAL HANDLING FOR ENG_CHN: Don't over-normalize Chinese content
                    string formattedDefinition;
                    if (sourceCode == "ENG_CHN")
                    {
                        // For ENG_CHN, preserve original formatting as much as possible
                        formattedDefinition = inputDefinition;

                        // Only apply minimal formatting - preserve Chinese characters
                        formattedDefinition = formatter.FormatDefinition(formattedDefinition);

                        logger.LogDebug(
                            "ENG_CHN definition processing | Word={Word} | OriginalLength={OrigLen} | FormattedLength={FormattedLen} | HasChinese={HasChinese}",
                            entry.Word,
                            inputDefinition.Length,
                            formattedDefinition.Length,
                            SimpleEngChnExtractor.ContainsChinese(formattedDefinition));
                    }
                    else
                    {
                        formattedDefinition = formatter.FormatDefinition(inputDefinition);
                    }

                    var correctedDefinition =
                        await grammarText.NormalizeDefinitionAsync(formattedDefinition, ct);

                    currentParsed = new ParsedDefinition
                    {
                        ParentKey = currentParsed.ParentKey,
                        SelfKey = currentParsed.SelfKey,
                        MeaningTitle = currentParsed.MeaningTitle,
                        SenseNumber = currentParsed.SenseNumber,
                        Definition = string.IsNullOrWhiteSpace(correctedDefinition)
                            ? formattedDefinition  // Use formatted instead of input
                            : correctedDefinition,
                        RawFragment = currentParsed.RawFragment,
                        Domain = currentParsed.Domain,
                        UsageLabel = currentParsed.UsageLabel,
                        Alias = currentParsed.Alias,
                        Synonyms = currentParsed.Synonyms,
                        CrossReferences = currentParsed.CrossReferences
                    };
                }

                var parsedId = await parsedWriter.WriteAsync(
                    entry.DictionaryEntryId,
                    currentParsed,
                    null,
                    ct);

                if (parsedId <= 0)
                {
                    logger.LogError(
                        "ParsedDefinition insert failed for DictionaryEntryId={DictionaryEntryId}, Word={Word}",
                        entry.DictionaryEntryId,
                        entry.Word);
                    continue; // Skip this entry but continue processing others
                }

                parsedInserted++;

                // ✅ Examples are strings in your project
                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var examples = exampleExtractor.Extract(currentParsed);

                    foreach (var exampleText in examples)
                    {
                        ct.ThrowIfCancellationRequested();

                        var formattedExample = formatter.FormatExample(exampleText);

                        var correctedExample =
                            await grammarText.NormalizeExampleAsync(formattedExample, ct);

                        await exampleWriter.WriteAsync(
                            parsedId,
                            correctedExample,
                            sourceCode,
                            ct);

                        exampleInserted++;
                    }

                    if (examples.Count > 0)
                        logger.LogDebug(
                            "Extracted {Count} examples for parsed definition {ParsedId} | Word={Word}",
                            examples.Count,
                            parsedId,
                            entry.Word);
                }

                // Synonyms
                if (!string.IsNullOrWhiteSpace(currentParsed.Definition))
                {
                    var synonymExtractor = synonymExtractorRegistry.GetExtractor(sourceCode);
                    var synonymResults = synonymExtractor.Extract(
                        entry.Word,
                        currentParsed.Definition,
                        currentParsed.RawFragment);

                    var validSynonyms = new List<string>();

                    foreach (var synonymResult in synonymResults)
                    {
                        if (synonymResult.ConfidenceLevel != "high" &&
                            synonymResult.ConfidenceLevel != "medium")
                            continue;

                        if (!synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword))
                            continue;
                        var cleanedSynonym = formatter.FormatSynonym(synonymResult.TargetHeadword);
                        if (!string.IsNullOrWhiteSpace(cleanedSynonym))
                            validSynonyms.Add(cleanedSynonym);

                        logger.LogDebug(
                            "Synonym detected | Source={Source} | Headword={Headword} | Synonym={Synonym} | Confidence={Confidence}",
                            sourceCode,
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

                // Etymology extraction
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
                            "Etymology extracted from definition | Source={Source} | Headword={Headword} | Method={Method}",
                            sourceCode,
                            entry.Word,
                            etymologyResult.DetectionMethod);

                        if (!string.IsNullOrWhiteSpace(etymologyResult.CleanedDefinition))
                        {
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
                }

                // Cross references
                foreach (var cr in currentParsed.CrossReferences)
                {
                    await crossRefWriter.WriteAsync(
                        parsedId,
                        cr,
                        ct);

                    crossRefInserted++;
                }

                // Alias
                if (!string.IsNullOrWhiteSpace(currentParsed.Alias))
                {
                    await aliasWriter.WriteAsync(
                        parsedId,
                        currentParsed.Alias,
                        ct);

                    aliasInserted++;
                }

                // ✅ Log ENG_CHN specific details for debugging
                if (sourceCode == "ENG_CHN" && parsedInserted % 100 == 0)
                {
                    var hasChinese = SimpleEngChnExtractor.ContainsChinese(currentParsed.Definition ?? "");
                    logger.LogDebug(
                        "ENG_CHN processing | Word={Word} | DefinitionPreview={Preview} | HasChinese={HasChinese} | Length={Length}",
                        entry.Word,
                        GetPreview(currentParsed.Definition, 30),
                        hasChinese,
                        currentParsed.Definition?.Length ?? 0);
                }
            }
        }

        // ✅ Final single completion log with ENG_CHN specific info
        if (sourceCode == "ENG_CHN")
        {
            logger.LogInformation(
                "Stage=Parsing completed | Source=ENG_CHN | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Examples={Examples} | Synonyms={Synonyms} | EtymologyExtracted={Etymology} | ENG_CHN_Fixed={EngChnFixed}",
                entryIndex,
                parsedInserted,
                crossRefInserted,
                exampleInserted,
                synonymInserted,
                etymologyExtracted,
                engChnFixedCount);
        }
        else
        {
            logger.LogInformation(
                "Stage=Parsing completed | Source={SourceCode} | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants} | Examples={Examples} | Synonyms={Synonyms} | EtymologyExtracted={Etymology}",
                sourceCode,
                entryIndex,
                parsedInserted,
                crossRefInserted,
                aliasInserted,
                variantInserted,
                exampleInserted,
                synonymInserted,
                etymologyExtracted);
        }
    }

    private string GetPreview(string text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) return "[empty]";
        if (text.Length <= length) return text;
        return text.Substring(0, length) + "...";
    }
}