// File: Infrastructure/Parsing/GenericSourceProcessor.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Core.Domain.Models;
using DictionaryImporter.Core.Text;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class GenericSourceProcessor(
    ILogger<GenericSourceProcessor> logger,
    IDictionaryTextFormatter formatter,
    IGrammarEnrichedTextService grammarText,
    IDictionaryEntryExampleWriter exampleWriter,
    IDictionaryEntrySynonymWriter synonymWriter,
    IEntryEtymologyWriter etymologyWriter,
    IExampleExtractorRegistry exampleExtractorRegistry,
    ISynonymExtractorRegistry synonymExtractorRegistry,
    IEtymologyExtractorRegistry etymologyExtractorRegistry)
    : ISourceSpecificProcessor
{
    private readonly ILogger<GenericSourceProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IDictionaryTextFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly IGrammarEnrichedTextService _grammarText = grammarText ?? throw new ArgumentNullException(nameof(grammarText));
    private readonly IDictionaryEntryExampleWriter _exampleWriter = exampleWriter ?? throw new ArgumentNullException(nameof(exampleWriter));
    private readonly IDictionaryEntrySynonymWriter _synonymWriter = synonymWriter ?? throw new ArgumentNullException(nameof(synonymWriter));
    private readonly IEntryEtymologyWriter _etymologyWriter = etymologyWriter ?? throw new ArgumentNullException(nameof(etymologyWriter));
    private readonly IExampleExtractorRegistry _exampleExtractorRegistry = exampleExtractorRegistry ?? throw new ArgumentNullException(nameof(exampleExtractorRegistry));
    private readonly ISynonymExtractorRegistry _synonymExtractorRegistry = synonymExtractorRegistry ?? throw new ArgumentNullException(nameof(synonymExtractorRegistry));
    private readonly IEtymologyExtractorRegistry _etymologyExtractorRegistry = etymologyExtractorRegistry ?? throw new ArgumentNullException(nameof(etymologyExtractorRegistry));

    public bool CanHandle(string sourceCode) => true;

    public async Task<ProcessingResult> ProcessEntryAsync(
        DictionaryEntry entry,
        ParsedDefinition parsed,
        long parsedId,
        CancellationToken ct)
    {
        var result = new ProcessingResult();

        if (entry is null)
            return result;

        if (parsed is null)
            return result;

        if (parsedId <= 0)
            return result;

        var sourceCode = string.IsNullOrWhiteSpace(entry.SourceCode)
            ? "UNKNOWN"
            : entry.SourceCode.Trim();

        if (string.IsNullOrWhiteSpace(parsed.Definition))
            return result;

        await ExtractAndSaveExamplesAsync(parsed, parsedId, sourceCode, result, ct);
        await ExtractAndSaveSynonymsAsync(entry, parsed, parsedId, sourceCode, result, ct);
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

            var rawExamples = exampleExtractor.Extract(parsed) ?? new List<string>();
            if (rawExamples.Count == 0)
                return;

            var examples = rawExamples
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var exampleText in examples)
            {
                ct.ThrowIfCancellationRequested();

                var formatted = _formatter.FormatExample(exampleText!);
                if (string.IsNullOrWhiteSpace(formatted))
                    continue;

                var corrected = await _grammarText.NormalizeExampleAsync(formatted, ct);
                var finalText = string.IsNullOrWhiteSpace(corrected) ? formatted : corrected;

                await _exampleWriter.WriteAsync(parsedId, finalText, sourceCode, ct);
                result.ExampleInserted++;
            }

            _logger.LogDebug(
                "Extracted {Count} examples for parsed definition {ParsedId}",
                result.ExampleInserted,
                parsedId);
        }
        catch (OperationCanceledException)
        {
            throw;
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

            IReadOnlyList<SynonymDetectionResult>? synonymResults =
                synonymExtractor.Extract(entry.Word, parsed.Definition, parsed.RawFragment);

            if (synonymResults == null || synonymResults.Count == 0)
                return;

            var validSynonyms = new List<string>(synonymResults.Count);

            foreach (var synonymResult in synonymResults)
            {
                ct.ThrowIfCancellationRequested();

                if (synonymResult == null)
                    continue;

                if (synonymResult.ConfidenceLevel is not ("high" or "medium"))
                    continue;

                if (!synonymExtractor.ValidateSynonymPair(entry.Word, synonymResult.TargetHeadword))
                    continue;

                var cleanedSynonym = _formatter.FormatSynonym(synonymResult.TargetHeadword ?? string.Empty);
                if (string.IsNullOrWhiteSpace(cleanedSynonym))
                    continue;

                validSynonyms.Add(cleanedSynonym);

                _logger.LogDebug(
                    "Synonym detected | Headword={Headword} | Synonym={Synonym} | Confidence={Confidence}",
                    entry.Word,
                    synonymResult.TargetHeadword,
                    synonymResult.ConfidenceLevel);
            }

            if (validSynonyms.Count == 0)
            {
                var def = parsed.Definition ?? string.Empty;

                _logger.LogDebug(
                    "No synonyms found for {Headword} | Definition preview: {Preview}",
                    entry.Word,
                    def.Substring(0, Math.Min(100, def.Length)));

                return;
            }

            var deduped = validSynonyms
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _synonymWriter.WriteSynonymsForParsedDefinition(
                parsedId,
                deduped,
                sourceCode,
                ct);

            result.SynonymInserted += deduped.Count;

            _logger.LogInformation(
                "Stored {Count} synonyms for ParsedId={ParsedId} | Headword={Headword}",
                deduped.Count,
                parsedId,
                entry.Word);
        }
        catch (OperationCanceledException)
        {
            throw;
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
                entry.Word,
                parsed.Definition,
                parsed.RawFragment);

            if (etymologyResult == null)
                return;

            if (string.IsNullOrWhiteSpace(etymologyResult.EtymologyText))
                return;

            var formatted = _formatter.FormatEtymology(etymologyResult.EtymologyText);
            if (string.IsNullOrWhiteSpace(formatted))
                return;

            await _etymologyWriter.WriteAsync(
                new DictionaryEntryEtymology
                {
                    DictionaryEntryId = entry.DictionaryEntryId,
                    EtymologyText = formatted,
                    LanguageCode = etymologyResult.LanguageCode,
                    SourceCode = sourceCode,
                    CreatedUtc = DateTime.UtcNow
                },
                ct);

            result.EtymologyExtracted++;

            _logger.LogDebug(
                "Etymology extracted | Headword={Headword} | Method={Method} | SourceCode={SourceCode}",
                entry.Word,
                etymologyResult.DetectionMethod,
                sourceCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to extract etymology for DictionaryEntryId={EntryId}",
                entry.DictionaryEntryId);
        }
    }
}