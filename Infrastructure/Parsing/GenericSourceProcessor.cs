using Microsoft.Extensions.Logging;
using DictionaryImporter.Core.Text;

namespace DictionaryImporter.Infrastructure.Parsing
{
    public sealed class GenericSourceProcessor : ISourceSpecificProcessor
    {
        private readonly ILogger<GenericSourceProcessor> _logger;
        private readonly IDictionaryTextFormatter _formatter;
        private readonly IGrammarEnrichedTextService _grammarText;
        private readonly IDictionaryEntryExampleWriter _exampleWriter;
        private readonly IDictionaryEntrySynonymWriter _synonymWriter;
        private readonly IEntryEtymologyWriter _etymologyWriter;
        private readonly IExampleExtractorRegistry _exampleExtractorRegistry;
        private readonly ISynonymExtractorRegistry _synonymExtractorRegistry;
        private readonly IEtymologyExtractorRegistry _etymologyExtractorRegistry;

        public GenericSourceProcessor(
            ILogger<GenericSourceProcessor> logger,
            IDictionaryTextFormatter formatter,
            IGrammarEnrichedTextService grammarText,
            IDictionaryEntryExampleWriter exampleWriter,
            IDictionaryEntrySynonymWriter synonymWriter,
            IEntryEtymologyWriter etymologyWriter,
            IExampleExtractorRegistry exampleExtractorRegistry,
            ISynonymExtractorRegistry synonymExtractorRegistry,
            IEtymologyExtractorRegistry etymologyExtractorRegistry)
        {
            _logger = logger;
            _formatter = formatter;
            _grammarText = grammarText;
            _exampleWriter = exampleWriter;
            _synonymWriter = synonymWriter;
            _etymologyWriter = etymologyWriter;
            _exampleExtractorRegistry = exampleExtractorRegistry;
            _synonymExtractorRegistry = synonymExtractorRegistry;
            _etymologyExtractorRegistry = etymologyExtractorRegistry;
        }

        public bool CanHandle(string sourceCode) => true; // Default handler

        public async Task<ProcessingResult> ProcessEntryAsync(
            DictionaryEntry entry,
            ParsedDefinition parsed,
            long parsedId,
            CancellationToken ct)
        {
            var result = new ProcessingResult();

            if (string.IsNullOrWhiteSpace(parsed.Definition))
                return result;

            var sourceCode = entry.SourceCode;

            // Extract and save examples
            await ExtractAndSaveExamplesAsync(parsed, parsedId, sourceCode, result, ct);

            // Extract and save synonyms
            await ExtractAndSaveSynonymsAsync(entry, parsed, parsedId, sourceCode, result, ct);

            // Extract and save etymology
            await ExtractAndSaveEtymologyAsync(entry, parsed, sourceCode, result, ct);

            return result;
        }

        private async Task ExtractAndSaveExamplesAsync(
            ParsedDefinition parsed,
            long parsedId,
            string sourceCode,
            ProcessingResult result,
            CancellationToken ct)
        {
            try
            {
                var exampleExtractor = _exampleExtractorRegistry.GetExtractor(sourceCode);
                if (exampleExtractor == null)
                {
                    _logger.LogDebug("No example extractor found for source {SourceCode}", sourceCode);
                    return;
                }

                var examples = exampleExtractor.Extract(parsed);

                foreach (var exampleText in examples)
                {
                    ct.ThrowIfCancellationRequested();

                    var formattedExample = _formatter.FormatExample(exampleText);
                    var correctedExample = await _grammarText.NormalizeExampleAsync(formattedExample, ct);

                    await _exampleWriter.WriteAsync(parsedId, correctedExample, sourceCode, ct);
                    result.ExampleInserted++;
                }

                if (examples.Count > 0)
                    _logger.LogDebug(
                        "Extracted {Count} examples for parsed definition {ParsedId}",
                        examples.Count, parsedId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract examples for parsed definition {ParsedId}", parsedId);
            }
        }

        private async Task ExtractAndSaveSynonymsAsync(
            DictionaryEntry entry,
            ParsedDefinition parsed,
            long parsedId,
            string sourceCode,
            ProcessingResult result,
            CancellationToken ct)
        {
            try
            {
                var synonymExtractor = _synonymExtractorRegistry.GetExtractor(sourceCode);
                if (synonymExtractor == null)
                {
                    _logger.LogDebug("No synonym extractor found for source {SourceCode}", sourceCode);
                    return;
                }

                var synonymResults = synonymExtractor.Extract(
                    entry.Word, parsed.Definition, parsed.RawFragment);

                var validSynonyms = new List<string>();
                foreach (var synonymResult in synonymResults)
                {
                    if (synonymResult.ConfidenceLevel is not ("high" or "medium"))
                        continue;

                    if (!synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword))
                        continue;

                    var cleanedSynonym = _formatter.FormatSynonym(synonymResult.TargetHeadword);
                    if (!string.IsNullOrWhiteSpace(cleanedSynonym))
                        validSynonyms.Add(cleanedSynonym);

                    _logger.LogDebug(
                        "Synonym detected | Headword={Headword} | Synonym={Synonym} | Confidence={Confidence}",
                        entry.Word, synonymResult.TargetHeadword, synonymResult.ConfidenceLevel);
                }

                if (validSynonyms.Count > 0)
                {
                    _logger.LogInformation(
                        "Found {Count} synonyms for {Headword}: {Synonyms}",
                        validSynonyms.Count, entry.Word, string.Join(", ", validSynonyms));

                    await _synonymWriter.WriteSynonymsForParsedDefinition(
                        parsedId, validSynonyms, sourceCode, ct);
                    result.SynonymInserted += validSynonyms.Count;
                }
                else
                {
                    _logger.LogDebug(
                        "No synonyms found for {Headword} | Definition preview: {Preview}",
                        entry.Word,
                        parsed.Definition.Substring(0, Math.Min(100, parsed.Definition.Length)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract synonyms for parsed definition {ParsedId}", parsedId);
            }
        }

        private async Task ExtractAndSaveEtymologyAsync(
            DictionaryEntry entry,
            ParsedDefinition parsed,
            string sourceCode,
            ProcessingResult result,
            CancellationToken ct)
        {
            try
            {
                var etymologyExtractor = _etymologyExtractorRegistry.GetExtractor(sourceCode);
                if (etymologyExtractor == null)
                {
                    _logger.LogDebug("No etymology extractor found for source {SourceCode}", sourceCode);
                    return;
                }

                var etymologyResult = etymologyExtractor.Extract(
                    entry.Word, parsed.Definition, parsed.RawFragment);

                if (string.IsNullOrWhiteSpace(etymologyResult.EtymologyText))
                    return;

                await _etymologyWriter.WriteAsync(
                    new DictionaryEntryEtymology
                    {
                        DictionaryEntryId = entry.DictionaryEntryId,
                        EtymologyText = etymologyResult.EtymologyText,
                        LanguageCode = etymologyResult.LanguageCode,
                        CreatedUtc = DateTime.UtcNow
                    },
                    ct);

                result.EtymologyExtracted++;

                _logger.LogDebug(
                    "Etymology extracted from definition | Headword={Headword} | Method={Method}",
                    entry.Word, etymologyResult.DetectionMethod);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract etymology for parsed definition {ParsedId}", entry.DictionaryEntryId);
            }
        }
    }
}