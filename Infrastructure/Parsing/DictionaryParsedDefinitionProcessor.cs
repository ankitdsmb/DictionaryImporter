using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using DictionaryImporter.Core.Text;
using DictionaryImporter.Core.PreProcessing;
using System.Text.Json;

// Fix ambiguous reference by using fully qualified name
using CoreIpaNormalizer = DictionaryImporter.Core.PreProcessing.IpaNormalizer;

namespace DictionaryImporter.Infrastructure.Parsing
{
    /// <summary>
    /// Processes dictionary entries through parsing pipeline:
    /// 1. Extracts clean definitions from raw data
    /// 2. Applies grammar correction
    /// 3. Extracts and saves related data (examples, synonyms, etymology, etc.)
    /// 4. Handles source-specific data extraction (Kaikki JSON, etc.)
    /// </summary>
    public sealed class DictionaryParsedDefinitionProcessor : IParsedDefinitionProcessor
    {
        private readonly string _connectionString;
        private readonly IDictionaryDefinitionParser _parser;
        private readonly SqlParsedDefinitionWriter _parsedWriter;
        private readonly SqlDictionaryEntryCrossReferenceWriter _crossRefWriter;
        private readonly SqlDictionaryAliasWriter _aliasWriter;
        private readonly IEntryEtymologyWriter _etymologyWriter;
        private readonly SqlDictionaryEntryVariantWriter _variantWriter;
        private readonly IDictionaryEntryExampleWriter _exampleWriter;
        private readonly IExampleExtractorRegistry _exampleExtractorRegistry;
        private readonly ISynonymExtractorRegistry _synonymExtractorRegistry;
        private readonly IDictionaryEntrySynonymWriter _synonymWriter;
        private readonly IEtymologyExtractorRegistry _etymologyExtractorRegistry;
        private readonly IDictionaryTextFormatter _formatter;
        private readonly IGrammarEnrichedTextService _grammarText;
        private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;
        private readonly KaikkiDataExtractor _kaikkiExtractor;

        // Updated constructor with 16 parameters (matching original)
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
            IEtymologyExtractorRegistry etymologyExtractorRegistry,
            IDictionaryTextFormatter formatter,
            IGrammarEnrichedTextService grammarText,
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
            _synonymWriter = synonymWriter;
            _etymologyExtractorRegistry = etymologyExtractorRegistry;
            _formatter = formatter;
            _grammarText = grammarText;
            _logger = logger;
            _kaikkiExtractor = new KaikkiDataExtractor(_logger);
        }

        public async Task ExecuteAsync(string sourceCode, CancellationToken ct)
        {
            _logger.LogInformation("Stage=Parsing started | Source={SourceCode}", sourceCode);

            var entries = await LoadEntriesAsync(sourceCode, ct);
            _logger.LogInformation(
                "Stage=Parsing | EntriesLoaded={Count} | Source={SourceCode}",
                entries.Count, sourceCode);

            var processor = new EntryProcessor(sourceCode, this);
            var result = await processor.ProcessEntriesAsync(entries, ct);

            LogCompletion(result, sourceCode);
        }

        private async Task<List<DictionaryEntry>> LoadEntriesAsync(string sourceCode, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            return (await conn.QueryAsync<DictionaryEntry>(
                """
                SELECT *
                FROM dbo.DictionaryEntry
                WHERE SourceCode = @SourceCode
                """,
                new { SourceCode = sourceCode }))
                .ToList();
        }

        private void LogCompletion(ProcessingResult result, string sourceCode)
        {
            _logger.LogInformation(
                """
                Stage=Parsing completed |
                Source={SourceCode} |
                Entries={Entries} |
                ParsedInserted={Parsed} |
                CrossRefs={CrossRefs} |
                Aliases={Aliases} |
                Examples={Examples} |
                Synonyms={Synonyms} |
                EtymologyExtracted={Etymology} |
                IPAExtracted={IPA} |
                AudioExtracted={Audio}
                """,
                sourceCode,
                result.TotalEntries,
                result.ParsedInserted,
                result.CrossRefInserted,
                result.AliasInserted,
                result.ExampleInserted,
                result.SynonymInserted,
                result.EtymologyExtracted,
                result.IpaExtracted,
                result.AudioExtracted);
        }

        #region Nested Classes for Better Organization

        private class EntryProcessor
        {
            private readonly string _sourceCode;
            private readonly DictionaryParsedDefinitionProcessor _parent;

            private readonly IExampleExtractor _exampleExtractor;
            private ProcessingResult _result = new();

            public EntryProcessor(string sourceCode, DictionaryParsedDefinitionProcessor parent)
            {
                _sourceCode = sourceCode;
                _parent = parent;
                _exampleExtractor = parent._exampleExtractorRegistry.GetExtractor(sourceCode);

                parent._logger.LogDebug(
                    "Using example extractor: {ExtractorType} for source {Source}",
                    _exampleExtractor.GetType().Name, sourceCode);
            }

            public async Task<ProcessingResult> ProcessEntriesAsync(
                List<DictionaryEntry> entries,
                CancellationToken ct)
            {
                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    _result.TotalEntries++;

                    if (_result.TotalEntries % 1_000 == 0)
                        LogProgress();

                    await ProcessEntryAsync(entry, ct);
                }

                return _result;
            }

            private void LogProgress()
            {
                _parent._logger.LogInformation(
                    """
                    Stage=Parsing progress |
                    Source={SourceCode} |
                    Entries={Entries} |
                    ParsedInserted={Parsed} |
                    CrossRefs={CrossRefs} |
                    Aliases={Aliases} |
                    Examples={Examples} |
                    Synonyms={Synonyms} |
                    Etymology={Etymology} |
                    IPA={IPA} |
                    Audio={Audio}
                    """,
                    _sourceCode,
                    _result.TotalEntries,
                    _result.ParsedInserted,
                    _result.CrossRefInserted,
                    _result.AliasInserted,
                    _result.ExampleInserted,
                    _result.SynonymInserted,
                    _result.EtymologyExtracted,
                    _result.IpaExtracted,
                    _result.AudioExtracted);
            }

            private async Task ProcessEntryAsync(DictionaryEntry entry, CancellationToken ct)
            {
                var parsedDefinitions = _parent._parser.Parse(entry)?.ToList()
                    ?? CreateDefaultParsedDefinition(entry);

                ValidateParsedDefinitions(parsedDefinitions, entry);

                foreach (var parsed in parsedDefinitions)
                {
                    await ProcessParsedDefinitionAsync(entry, parsed, ct);
                }
            }

            private async Task ProcessParsedDefinitionAsync(
                DictionaryEntry entry,
                ParsedDefinition parsed,
                CancellationToken ct)
            {
                var processed = await ProcessDefinitionTextAsync(parsed, ct);
                var parsedId = await SaveParsedDefinitionAsync(entry, processed, ct);

                if (parsedId <= 0)
                    throw new InvalidOperationException(
                        $"ParsedDefinition insert failed for DictionaryEntryId={entry.DictionaryEntryId}");

                _result.ParsedInserted++;

                await ExtractAndSaveAllDataAsync(entry, processed, parsedId, ct);
            }

            private async Task<ParsedDefinition> ProcessDefinitionTextAsync(
                ParsedDefinition parsed,
                CancellationToken ct)
            {
                var extractedDefinition = ExtractCleanDefinition(parsed, _sourceCode);

                // Create a new ParsedDefinition with updated properties
                var processed = new ParsedDefinition
                {
                    ParentKey = parsed.ParentKey,
                    SelfKey = parsed.SelfKey,
                    MeaningTitle = parsed.MeaningTitle,
                    SenseNumber = parsed.SenseNumber,
                    Definition = !string.IsNullOrWhiteSpace(extractedDefinition) ? extractedDefinition : parsed.Definition,
                    RawFragment = parsed.RawFragment,
                    Domain = parsed.Domain,
                    UsageLabel = parsed.UsageLabel,
                    Alias = parsed.Alias,
                    Synonyms = parsed.Synonyms,
                    CrossReferences = parsed.CrossReferences
                };

                var inputDefinition = processed.Definition ?? processed.RawFragment;
                if (string.IsNullOrWhiteSpace(inputDefinition))
                    return processed;

                var formattedDefinition = _parent._formatter.FormatDefinition(inputDefinition);
                var correctedDefinition = await _parent._grammarText.NormalizeDefinitionAsync(formattedDefinition, ct);

                // Update the definition with corrected version
                processed.Definition = string.IsNullOrWhiteSpace(correctedDefinition)
                    ? inputDefinition
                    : correctedDefinition;

                return processed;
            }

            private async Task<long> SaveParsedDefinitionAsync(
                DictionaryEntry entry,
                ParsedDefinition parsed,
                CancellationToken ct)
            {
                return await _parent._parsedWriter.WriteAsync(
                    entry.DictionaryEntryId,
                    parsed,
                    null,
                    ct);
            }

            private async Task ExtractAndSaveAllDataAsync(
                DictionaryEntry entry,
                ParsedDefinition parsed,
                long parsedId,
                CancellationToken ct)
            {
                // Extract pronunciation data (IPA, Audio)
                if (!string.IsNullOrWhiteSpace(parsed.RawFragment))
                {
                    var (ipaCount, audioCount) = await _parent._kaikkiExtractor.ExtractAndSavePronunciationAsync(
                        entry, parsed.RawFragment, _sourceCode, _parent._connectionString, ct);
                    _result.IpaExtracted += ipaCount;
                    _result.AudioExtracted += audioCount;
                }

                // Extract and save examples
                await ExtractAndSaveExamplesAsync(parsed, parsedId, ct);

                // Extract and save synonyms
                await ExtractAndSaveSynonymsAsync(entry, parsed, parsedId, ct);

                // Extract and save etymology
                await ExtractAndSaveEtymologyAsync(entry, parsed, ct);

                // Extract and save cross-references
                await ExtractAndSaveCrossReferencesAsync(parsed, parsedId, ct);

                // Extract and save aliases
                await ExtractAndSaveAliasesAsync(parsed, parsedId, ct);
            }

            private async Task ExtractAndSaveExamplesAsync(
                ParsedDefinition parsed,
                long parsedId,
                CancellationToken ct)
            {
                if (string.IsNullOrWhiteSpace(parsed.Definition))
                    return;

                var examples = _exampleExtractor.Extract(parsed);

                // Kaikki-specific extraction
                if (examples.Count == 0 && _sourceCode == "KAIKKI")
                    examples = _parent._kaikkiExtractor.ExtractExamplesFromJson(parsed.RawFragment);

                foreach (var exampleText in examples)
                {
                    ct.ThrowIfCancellationRequested();

                    var formattedExample = _parent._formatter.FormatExample(exampleText);
                    var correctedExample = await _parent._grammarText.NormalizeExampleAsync(formattedExample, ct);

                    await _parent._exampleWriter.WriteAsync(parsedId, correctedExample, _sourceCode, ct);
                    _result.ExampleInserted++;
                }

                if (examples.Count > 0)
                    _parent._logger.LogDebug(
                        "Extracted {Count} examples for parsed definition {ParsedId}",
                        examples.Count, parsedId);
            }

            private async Task ExtractAndSaveSynonymsAsync(
                DictionaryEntry entry,
                ParsedDefinition parsed,
                long parsedId,
                CancellationToken ct)
            {
                if (string.IsNullOrWhiteSpace(parsed.Definition))
                    return;

                var synonymExtractor = _parent._synonymExtractorRegistry.GetExtractor(_sourceCode);
                var synonymResults = synonymExtractor.Extract(
                    entry.Word, parsed.Definition, parsed.RawFragment);

                var validSynonyms = new List<string>();
                foreach (var synonymResult in synonymResults)
                {
                    if (synonymResult.ConfidenceLevel is not ("high" or "medium"))
                        continue;

                    if (!synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword))
                        continue;

                    var cleanedSynonym = _parent._formatter.FormatSynonym(synonymResult.TargetHeadword);
                    if (!string.IsNullOrWhiteSpace(cleanedSynonym))
                        validSynonyms.Add(cleanedSynonym);

                    _parent._logger.LogDebug(
                        "Synonym detected | Headword={Headword} | Synonym={Synonym} | Confidence={Confidence}",
                        entry.Word, synonymResult.TargetHeadword, synonymResult.ConfidenceLevel);
                }

                if (validSynonyms.Count > 0)
                {
                    _parent._logger.LogInformation(
                        "Found {Count} synonyms for {Headword}: {Synonyms}",
                        validSynonyms.Count, entry.Word, string.Join(", ", validSynonyms));

                    await _parent._synonymWriter.WriteSynonymsForParsedDefinition(
                        parsedId, validSynonyms, _sourceCode, ct);
                    _result.SynonymInserted += validSynonyms.Count;
                }
                else
                {
                    _parent._logger.LogDebug(
                        "No synonyms found for {Headword} | Definition preview: {Preview}",
                        entry.Word,
                        parsed.Definition.Substring(0, Math.Min(100, parsed.Definition.Length)));
                }
            }

            private async Task ExtractAndSaveEtymologyAsync(
                DictionaryEntry entry,
                ParsedDefinition parsed,
                CancellationToken ct)
            {
                if (string.IsNullOrWhiteSpace(parsed.Definition))
                    return;

                var etymologyExtractor = _parent._etymologyExtractorRegistry.GetExtractor(_sourceCode);
                var etymologyResult = etymologyExtractor.Extract(
                    entry.Word, parsed.Definition, parsed.RawFragment);

                if (string.IsNullOrWhiteSpace(etymologyResult.EtymologyText))
                    return;

                await _parent._etymologyWriter.WriteAsync(
                    new DictionaryEntryEtymology
                    {
                        DictionaryEntryId = entry.DictionaryEntryId,
                        EtymologyText = etymologyResult.EtymologyText,
                        LanguageCode = etymologyResult.LanguageCode,
                        CreatedUtc = DateTime.UtcNow
                    },
                    ct);

                _result.EtymologyExtracted++;

                _parent._logger.LogDebug(
                    "Etymology extracted from definition | Headword={Headword} | Method={Method}",
                    entry.Word, etymologyResult.DetectionMethod);

                // Update the parsed definition if etymology extraction cleaned it
                if (!string.IsNullOrWhiteSpace(etymologyResult.CleanedDefinition))
                {
                    // Note: We would need to update the database here
                    // For now, we'll just log it
                    _parent._logger.LogDebug(
                        "Etymology extraction cleaned definition for {Headword}",
                        entry.Word);
                }
            }

            private async Task ExtractAndSaveCrossReferencesAsync(
                ParsedDefinition parsed,
                long parsedId,
                CancellationToken ct)
            {
                if (parsed.CrossReferences != null)
                {
                    foreach (var cr in parsed.CrossReferences)
                    {
                        await _parent._crossRefWriter.WriteAsync(parsedId, cr, ct);
                        _result.CrossRefInserted++;
                    }
                }

                // Kaikki-specific cross-reference extraction
                if (_sourceCode == "KAIKKI")
                {
                    var additionalCrossRefs = await _parent._kaikkiExtractor.ExtractCrossReferencesFromJsonAsync(
                        parsedId, parsed.RawFragment, _parent._crossRefWriter, ct);
                    _result.CrossRefInserted += additionalCrossRefs;
                }
            }

            private async Task ExtractAndSaveAliasesAsync(
                ParsedDefinition parsed,
                long parsedId,
                CancellationToken ct)
            {
                if (!string.IsNullOrWhiteSpace(parsed.Alias))
                {
                    await _parent._aliasWriter.WriteAsync(parsedId, parsed.Alias, ct);
                    _result.AliasInserted++;
                }

                // Kaikki-specific alias extraction
                if (_sourceCode == "KAIKKI")
                {
                    var additionalAliases = await _parent._kaikkiExtractor.ExtractAliasesFromJsonAsync(
                        parsedId, parsed.RawFragment, _parent._aliasWriter, ct);
                    _result.AliasInserted += additionalAliases;
                }
            }

            private string? ExtractCleanDefinition(ParsedDefinition parsed, string sourceCode)
            {
                string? extractedDefinition = null;

                if (!string.IsNullOrWhiteSpace(parsed.Definition))
                {
                    if (sourceCode == "KAIKKI")
                        extractedDefinition = _parent._kaikkiExtractor.ExtractDefinitionFromJson(parsed.RawFragment);

                    if (string.IsNullOrWhiteSpace(extractedDefinition))
                        extractedDefinition = DefinitionExtractor.ExtractDefinitionFromFormattedText(parsed.Definition);
                }
                else if (!string.IsNullOrWhiteSpace(parsed.RawFragment))
                {
                    if (sourceCode == "KAIKKI")
                        extractedDefinition = _parent._kaikkiExtractor.ExtractDefinitionFromJson(parsed.RawFragment);

                    if (string.IsNullOrWhiteSpace(extractedDefinition))
                        extractedDefinition = DefinitionExtractor.ExtractDefinitionFromFormattedText(parsed.RawFragment);
                }

                return extractedDefinition;
            }

            private static List<ParsedDefinition> CreateDefaultParsedDefinition(DictionaryEntry entry)
            {
                return
                [
                    new ParsedDefinition
                    {
                        Definition = null,
                        RawFragment = entry.Definition,
                        SenseNumber = entry.SenseNumber
                    }
                ];
            }

            private static void ValidateParsedDefinitions(List<ParsedDefinition> parsedDefinitions, DictionaryEntry entry)
            {
                if (parsedDefinitions.Count != 1)
                    throw new InvalidOperationException(
                        $"Parser returned {parsedDefinitions.Count} ParsedDefinitions for DictionaryEntryId={entry.DictionaryEntryId}. Exactly 1 is required.");
            }
        }

        #endregion Nested Classes for Better Organization

        #region Helper Classes

        private class ProcessingResult
        {
            public int TotalEntries { get; set; }
            public int ParsedInserted { get; set; }
            public int CrossRefInserted { get; set; }
            public int AliasInserted { get; set; }
            public int ExampleInserted { get; set; }
            public int SynonymInserted { get; set; }
            public int EtymologyExtracted { get; set; }
            public int IpaExtracted { get; set; }
            public int AudioExtracted { get; set; }
        }

        // Inside DictionaryParsedDefinitionProcessor class
        private class KaikkiDataExtractor
        {
            private readonly ILogger _logger;

            private readonly JsonSerializerOptions _jsonOptions = new()
            {
                PropertyNameCaseInsensitive = true
            };

            public KaikkiDataExtractor(ILogger logger)
            {
                _logger = logger;
            }

            public async Task<(int IpaCount, int AudioCount)> ExtractAndSavePronunciationAsync(
                DictionaryEntry entry, string rawFragment, string sourceCode,
                string connectionString, CancellationToken ct)
            {
                if (sourceCode != "KAIKKI" || !IsJson(rawFragment)) return (0, 0);

                try
                {
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawFragment, _jsonOptions);
                    if (rawData == null || !rawData.TryGetValue("sounds", out var soundsElement))
                        return (0, 0);

                    var ipaCount = 0;
                    var audioCount = 0;

                    foreach (var sound in soundsElement.EnumerateArray())
                    {
                        // Extract IPA
                        if (sound.TryGetProperty("ipa", out var ipaProp) && ipaProp.ValueKind == JsonValueKind.String)
                        {
                            var ipa = ipaProp.GetString();
                            if (!string.IsNullOrWhiteSpace(ipa))
                            {
                                await SaveIpaPronunciationAsync(entry, ipa, connectionString, ct);
                                ipaCount++;
                            }
                        }

                        // Extract audio URL
                        if (sound.TryGetProperty("mp3_url", out var mp3Prop) && mp3Prop.ValueKind == JsonValueKind.String)
                        {
                            var audio = mp3Prop.GetString();
                            if (!string.IsNullOrWhiteSpace(audio))
                            {
                                await SaveAudioUrlAsync(entry, audio, connectionString, ct);
                                audioCount++;
                            }
                        }
                        else if (sound.TryGetProperty("ogg_url", out var oggProp) && oggProp.ValueKind == JsonValueKind.String)
                        {
                            var audio = oggProp.GetString();
                            if (!string.IsNullOrWhiteSpace(audio))
                            {
                                await SaveAudioUrlAsync(entry, audio, connectionString, ct);
                                audioCount++;
                            }
                        }
                    }

                    return (ipaCount, audioCount);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract pronunciation for word: {Word}", entry.Word);
                    return (0, 0);
                }
            }

            private async Task SaveIpaPronunciationAsync(
                DictionaryEntry entry, string ipa, string connectionString, CancellationToken ct)
            {
                try
                {
                    await using var conn = new SqlConnection(connectionString);
                    var canonicalWordId = await conn.ExecuteScalarAsync<long?>(
                        """
                SELECT CanonicalWordId
                FROM dbo.CanonicalWord
                WHERE NormalizedWord = @NormalizedWord
                """,
                        new { entry.NormalizedWord });

                    if (!canonicalWordId.HasValue)
                        return;

                    var normalizedIpa = CoreIpaNormalizer.Normalize(ipa);

                    await conn.ExecuteAsync(
                        """
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.CanonicalWordPronunciation
                    WHERE CanonicalWordId = @CanonicalWordId
                    AND Ipa = @Ipa
                )
                BEGIN
                    INSERT INTO dbo.CanonicalWordPronunciation
                    (CanonicalWordId, LocaleCode, Ipa, CreatedUtc)
                    VALUES (@CanonicalWordId, 'en', @Ipa, SYSUTCDATETIME())
                END
                """,
                        new { canonicalWordId, Ipa = normalizedIpa });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to save IPA pronunciation for word: {Word}", entry.Word);
                }
            }

            private async Task SaveAudioUrlAsync(
                DictionaryEntry entry, string audioUrl, string connectionString, CancellationToken ct)
            {
                try
                {
                    if (!audioUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        audioUrl = $"https:{audioUrl}";

                    await EnsureAudioTableExistsAsync(connectionString);

                    await using var conn = new SqlConnection(connectionString);
                    await conn.ExecuteAsync(
                        """
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.WordAudio
                    WHERE Word = @Word
                    AND AudioUrl = @AudioUrl
                )
                BEGIN
                    INSERT INTO dbo.WordAudio
                    (Word, AudioUrl, SourceCode, CreatedUtc)
                    VALUES (@Word, @AudioUrl, @SourceCode, SYSUTCDATETIME())
                END
                """,
                        new { entry.Word, AudioUrl = audioUrl, entry.SourceCode });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to save audio URL for word: {Word}", entry.Word);
                }
            }

            private async Task EnsureAudioTableExistsAsync(string connectionString)
            {
                try
                {
                    await using var conn = new SqlConnection(connectionString);
                    await conn.ExecuteAsync(
                        """
                IF NOT EXISTS (
                    SELECT 1
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_NAME = 'WordAudio'
                )
                BEGIN
                    CREATE TABLE dbo.WordAudio (
                        WordAudioId bigint IDENTITY(1,1) PRIMARY KEY,
                        Word nvarchar(200) NOT NULL,
                        AudioUrl nvarchar(500) NOT NULL,
                        SourceCode nvarchar(50) NOT NULL,
                        CreatedUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
                    )

                    CREATE INDEX IX_WordAudio_Word ON dbo.WordAudio (Word)
                    CREATE INDEX IX_WordAudio_SourceCode ON dbo.WordAudio (SourceCode)
                END
                """);
                }
                catch
                {
                    // Table might already exist or permissions issue
                }
            }

            public async Task<int> ExtractAliasesFromJsonAsync(
                long parsedId, string rawFragment, SqlDictionaryAliasWriter aliasWriter, CancellationToken ct)
            {
                if (!IsJson(rawFragment)) return 0;

                try
                {
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawFragment, _jsonOptions);
                    if (rawData == null || !rawData.TryGetValue("forms", out var formsElement))
                        return 0;

                    var count = 0;
                    foreach (var form in formsElement.EnumerateArray())
                    {
                        if (form.TryGetProperty("form", out var formProp) && formProp.ValueKind == JsonValueKind.String)
                        {
                            var alias = formProp.GetString();
                            if (!string.IsNullOrWhiteSpace(alias))
                            {
                                await aliasWriter.WriteAsync(parsedId, alias, ct);
                                count++;
                            }
                        }
                    }

                    return count;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract aliases from JSON");
                    return 0;
                }
            }

            public async Task<int> ExtractCrossReferencesFromJsonAsync(
                long parsedId, string rawFragment, SqlDictionaryEntryCrossReferenceWriter crossRefWriter, CancellationToken ct)
            {
                if (!IsJson(rawFragment)) return 0;

                try
                {
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawFragment, _jsonOptions);
                    if (rawData == null || !rawData.TryGetValue("related", out var relatedElement))
                        return 0;

                    var count = 0;
                    foreach (var related in relatedElement.EnumerateArray())
                    {
                        if (related.TryGetProperty("word", out var wordProp) && wordProp.ValueKind == JsonValueKind.String)
                        {
                            var word = wordProp.GetString();
                            var type = "related";

                            if (related.TryGetProperty("sense", out var senseProp) && senseProp.ValueKind == JsonValueKind.String)
                                type = senseProp.GetString() ?? "related";

                            if (!string.IsNullOrWhiteSpace(word))
                            {
                                await crossRefWriter.WriteAsync(
                                    parsedId,
                                    new CrossReference
                                    {
                                        TargetWord = word,
                                        ReferenceType = type
                                    },
                                    ct);
                                count++;
                            }
                        }
                    }

                    return count;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract cross-references from JSON");
                    return 0;
                }
            }

            public List<string> ExtractExamplesFromJson(string rawFragment)
            {
                var examples = new List<string>();
                if (!IsJson(rawFragment)) return examples;

                try
                {
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawFragment, _jsonOptions);
                    if (rawData == null || !rawData.TryGetValue("examples", out var examplesElement))
                        return examples;

                    foreach (var example in examplesElement.EnumerateArray())
                    {
                        if (example.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            var text = textProp.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                                examples.Add(text);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract examples from JSON");
                }

                return examples;
            }

            public string? ExtractDefinitionFromJson(string rawFragment)
            {
                if (!IsJson(rawFragment)) return null;

                try
                {
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawFragment, _jsonOptions);
                    if (rawData != null && rawData.TryGetValue("sense", out var senseElement))
                    {
                        if (senseElement.ValueKind == JsonValueKind.String)
                            return senseElement.GetString();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to extract definition from Kaikki JSON");
                }

                return null;
            }

            public bool IsEnglishEntry(string rawFragment)
            {
                if (!IsJson(rawFragment)) return false;

                try
                {
                    var rawData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawFragment, _jsonOptions);
                    if (rawData == null) return false;

                    // Check for English language
                    if (rawData.TryGetValue("lang_code", out var langCode))
                    {
                        if (langCode.ValueKind == JsonValueKind.String)
                            return langCode.GetString() == "en";
                    }

                    if (rawData.TryGetValue("lang", out var lang))
                    {
                        if (lang.ValueKind == JsonValueKind.String)
                        {
                            var langStr = lang.GetString();
                            return langStr == "English" || langStr?.Contains("english", StringComparison.OrdinalIgnoreCase) == true;
                        }
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            }

            private static bool IsJson(string text)
            {
                return !string.IsNullOrWhiteSpace(text) &&
                       text.Trim().StartsWith("{") &&
                       text.Trim().EndsWith("}");
            }
        }

        #endregion Helper Classes
    }

    /// <summary>
    /// Interface for parsed definition processors
    /// </summary>
    public interface IParsedDefinitionProcessor
    {
        Task ExecuteAsync(string sourceCode, CancellationToken ct);
    }
}