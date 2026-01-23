using DictionaryImporter.Common;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Sources.Parsing;
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
    private readonly IOcrArtifactNormalizer _ocrNormalizer;
    private readonly IDefinitionNormalizer _definitionNormalizer;
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
        IOcrArtifactNormalizer ocrNormalizer,
        IDefinitionNormalizer definitionNormalizer,
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
        _ocrNormalizer = ocrNormalizer ?? throw new ArgumentNullException(nameof(ocrNormalizer));
        _definitionNormalizer = definitionNormalizer ?? throw new ArgumentNullException(nameof(definitionNormalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(string sourceCode, CancellationToken ct)
    {
        sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode;

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

        _logger.LogInformation("Processing {Count} entries for source {SourceCode}", entries.Count, sourceCode);

        var entryIndex = 0;
        var parsedInserted = 0;
        var crossRefInserted = 0;
        var aliasInserted = 0;
        var exampleInserted = 0;
        var synonymInserted = 0;
        var etymologyExtracted = 0;
        var variantInserted = 0;

        var nonEnglishEntries = 0;
        var nonEnglishExamples = 0;
        var nonEnglishSynonyms = 0;
        var nonEnglishEtymology = 0;
        var nonEnglishAliases = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            entryIndex++;

            if (entryIndex % 1000 == 0)
            {
                _logger.LogInformation("Parsing progress | Source={SourceCode} | Processed={Processed}/{Total}", sourceCode, entryIndex, entries.Count);
            }

            var parser = _parserResolver.Resolve(sourceCode);
            var parsedDefinitions = parser.Parse(entry)?.ToList();

            if (parsedDefinitions == null || parsedDefinitions.Count == 0)
            {
                _logger.LogWarning("Parser returned empty for entry {Word} ({Source})", entry.Word, sourceCode);
                parsedDefinitions = new List<ParsedDefinition> { CreateFallbackDefinition(entry, sourceCode) };
            }

            foreach (var parsed in parsedDefinitions)
            {
                // 1. Process Definition Text
                var (processedDefinition, definitionNonEnglishTextId) = await ProcessTextContent(
                    parsed.Definition,
                    fieldType: "Definition",
                    sourceCode,
                    ct);

                // 2. Format Domain & Usage
                var rawDomain = Helper.ExtractProperDomain(sourceCode, parsed.Domain, parsed.Definition);
                var formattedDomain = _formatter.FormatDomain(rawDomain);
                var formattedUsageLabel = _formatter.FormatUsageLabel(parsed.UsageLabel);

                // 3. Clean RawFragment (HTML & Markers)
                // Using CleanHtml and RemoveFormattingMarkers
                var cleanedFragment = _formatter.CleanHtml(parsed.RawFragment);
                cleanedFragment = _formatter.RemoveFormattingMarkers(cleanedFragment);

                // 4. Normalize Headword Spacing
                // Using NormalizeSpacing
                var normalizedTitle = _formatter.NormalizeSpacing(parsed.MeaningTitle);

                var currentParsed = new ParsedDefinition
                {
                    ParentKey = parsed.ParentKey,
                    SelfKey = parsed.SelfKey,
                    MeaningTitle = normalizedTitle, // Normalized
                    SenseNumber = parsed.SenseNumber,
                    Definition = processedDefinition,
                    RawFragment = cleanedFragment,  // Cleaned
                    Domain = formattedDomain,
                    UsageLabel = formattedUsageLabel,
                    Alias = parsed.Alias,
                    Synonyms = parsed.Synonyms,
                    CrossReferences = parsed.CrossReferences,
                    SourceCode = sourceCode,
                    HasNonEnglishText = definitionNonEnglishTextId.HasValue,
                    NonEnglishTextId = definitionNonEnglishTextId
                };

                var parsedId = await _parsedWriter.WriteAsync(entry.DictionaryEntryId, currentParsed, sourceCode, ct);

                if (parsedId <= 0)
                {
                    _logger.LogError("Failed to insert parsed definition for entry {EntryId}, Word={Word}", entry.DictionaryEntryId, entry.Word);
                    continue;
                }

                parsedInserted++;
                if (definitionNonEnglishTextId.HasValue) nonEnglishEntries++;

                // --- Examples ---
                if (!string.IsNullOrWhiteSpace(parsed.Definition) || parsed.Examples?.Count > 0)
                {
                    var exampleExtractor = _exampleExtractorRegistry.GetExtractor(sourceCode);
                    var rawExamples = exampleExtractor.Extract(currentParsed) ?? new List<string>();

                    var examples = rawExamples
                        .Select(e => NormalizeForExampleDedupe(e))
                        .Where(e => !string.IsNullOrWhiteSpace(e) && !IsPlaceholderExample(e))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var exampleText in examples)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (processedExample, exampleNonEnglishTextId) = await ProcessTextContent(exampleText, fieldType: "Example", sourceCode, ct);

                        if (IsPlaceholderExample(processedExample)) continue;

                        await _exampleWriter.WriteAsync(parsedId, processedExample, sourceCode, ct);
                        exampleInserted++;
                        if (exampleNonEnglishTextId.HasValue) nonEnglishExamples++;
                    }
                }

                // --- Synonyms ---
                if (!string.IsNullOrWhiteSpace(parsed.Definition))
                {
                    var synonymExtractor = _synonymExtractorRegistry.GetExtractor(sourceCode);
                    var synonymResults = synonymExtractor.Extract(entry.Word, parsed.Definition, parsed.RawFragment);
                    var validSynonyms = new List<string>();

                    foreach (var synonymResult in synonymResults)
                    {
                        if (synonymResult.ConfidenceLevel is not ("high" or "medium")) continue;
                        if (!synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword)) continue;

                        var (processedSynonym, synonymNonEnglishTextId) = await ProcessTextContent(synonymResult.TargetHeadword, fieldType: "Synonym", sourceCode, ct);

                        if (IsPlaceholderSynonym(processedSynonym)) continue;

                        var cleanedSynonym = NormalizeForSynonymDedupe(processedSynonym);
                        if (!string.IsNullOrWhiteSpace(cleanedSynonym)) validSynonyms.Add(cleanedSynonym);
                        if (synonymNonEnglishTextId.HasValue) nonEnglishSynonyms++;
                    }

                    validSynonyms = validSynonyms.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (validSynonyms.Count > 0)
                    {
                        await _synonymWriter.WriteSynonymsForParsedDefinition(parsedId, validSynonyms, sourceCode, ct);
                        synonymInserted += validSynonyms.Count;
                    }
                }

                // --- Etymology ---
                if (!string.IsNullOrWhiteSpace(parsed.Definition))
                {
                    var etymologyExtractor = _etymologyExtractorRegistry.GetExtractor(sourceCode);
                    var etymologyResult = etymologyExtractor.Extract(entry.Word, parsed.Definition, parsed.RawFragment);

                    if (!string.IsNullOrWhiteSpace(etymologyResult.EtymologyText))
                    {
                        // Uses FormatEtymology inside ProcessTextContent
                        var (processedEtymology, etymologyNonEnglishTextId) = await ProcessTextContent(etymologyResult.EtymologyText, fieldType: "Etymology", sourceCode, ct);

                        await _etymologyWriter.WriteAsync(new DictionaryEntryEtymology
                        {
                            DictionaryEntryId = entry.DictionaryEntryId,
                            EtymologyText = processedEtymology,
                            LanguageCode = etymologyResult.LanguageCode,
                            SourceCode = sourceCode,
                            HasNonEnglishText = etymologyNonEnglishTextId.HasValue,
                            NonEnglishTextId = etymologyNonEnglishTextId,
                            CreatedUtc = DateTime.UtcNow
                        }, ct);

                        etymologyExtracted++;
                        if (etymologyNonEnglishTextId.HasValue) nonEnglishEtymology++;
                    }
                }

                // --- Cross References ---
                if (currentParsed.CrossReferences != null)
                {
                    foreach (var cr in currentParsed.CrossReferences)
                    {
                        // 5. Using FormatCrossReference for Trace Logging
                        if (_logger.IsEnabled(LogLevel.Trace))
                        {
                            var formattedRef = _formatter.FormatCrossReference(cr);
                            _logger.LogTrace("Processing CrossRef: {FormattedRef}", formattedRef);
                        }

                        await _crossRefWriter.WriteAsync(parsedId, cr, sourceCode, ct);
                        crossRefInserted++;
                    }
                }

                // --- Alias ---
                if (!string.IsNullOrWhiteSpace(currentParsed.Alias))
                {
                    var (processedAlias, aliasNonEnglishTextId) = await ProcessTextContent(currentParsed.Alias, fieldType: "Alias", sourceCode, ct);

                    await _aliasWriter.WriteAsync(parsedId, processedAlias, sourceCode, ct);
                    aliasInserted++;
                    if (aliasNonEnglishTextId.HasValue) nonEnglishAliases++;

                    if (!string.IsNullOrWhiteSpace(entry.Word) && !string.Equals(entry.Word.Trim(), currentParsed.Alias.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        await _variantWriter.WriteAsync(entry.DictionaryEntryId, currentParsed.Alias.Trim(), variantType: "alias", sourceCode: sourceCode, ct);
                        variantInserted++;
                    }
                }
            }
        }

        _logger.LogInformation("Stage=Parsing completed | Source={SourceCode} | Stats...", sourceCode);
    }

    private async Task<(string ProcessedText, long? NonEnglishTextId)> ProcessTextContent(
        string text,
        string fieldType,
        string sourceCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return (string.Empty, null);

        // 3. Apply OCR Normalization
        text = _ocrNormalizer.Normalize(text, "en");

        // 4. Apply Definition Normalization
        if (fieldType == "Definition")
        {
            text = _definitionNormalizer.Normalize(text);
        }

        var isBilingual = _languageDetectionService.IsBilingualText(text);
        var containsNonEnglish = _languageDetectionService.ContainsNonEnglish(text);

        if (!containsNonEnglish && !isBilingual)
        {
            // 6. Integrated ALL Formatter Methods via Switch
            string formattedText = fieldType switch
            {
                "Example" => _formatter.FormatExample(text),
                "Synonym" => _formatter.FormatSynonym(text),
                "Alias" => _formatter.FormatSynonym(text), // Alias treats like Synonym
                "Etymology" => _formatter.FormatEtymology(text),
                "Antonym" => _formatter.FormatAntonym(text), // Added for completeness
                "Note" => _formatter.FormatNote(text),       // Added for completeness
                _ => _formatter.FormatDefinition(text)
            };

            if (fieldType == "Example")
            {
                var normalized = await _grammarText.NormalizeExampleAsync(formattedText, ct);
                return (normalized ?? formattedText, null);
            }

            if (fieldType == "Definition")
            {
                var normalizedText = await _grammarText.NormalizeDefinitionAsync(formattedText, ct);
                return (normalizedText ?? formattedText, null);
            }

            return (formattedText, null);
        }

        var nonEnglishTextId = await _nonEnglishTextStorage.StoreNonEnglishTextAsync(text, sourceCode, fieldType, ct);
        var placeholder = isBilingual ? $"[BILINGUAL_{fieldType.ToUpperInvariant()}]" : $"[NON_ENGLISH_{fieldType.ToUpperInvariant()}]";
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

    // Helper methods (IsPlaceholderExample, etc.) remain the same as previous...
    private static bool IsPlaceholderExample(string text) => !string.IsNullOrWhiteSpace(text) && (text.StartsWith("[NON_ENGLISH_", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[BILINGUAL_", StringComparison.OrdinalIgnoreCase));
    private static bool IsPlaceholderSynonym(string text) => !string.IsNullOrWhiteSpace(text) && (text.StartsWith("[NON_ENGLISH_", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[BILINGUAL_", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeForExampleDedupe(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = Regex.Replace(text.Trim(), @"\s+", " ").Replace("’", "'").Trim('\"', '\'', '“', '”', '‘', '’', '.', ',', ';', ':', '!', '?');
        return t.Length > 800 ? t.Substring(0, 800).Trim() : t;
    }

    private static string NormalizeForSynonymDedupe(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = Regex.Replace(text.Trim(), @"\s+", " ").Replace("’", "'");
        return t.Length > 200 ? t.Substring(0, 200).Trim() : t;
    }
}