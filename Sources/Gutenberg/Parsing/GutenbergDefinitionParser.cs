using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Infrastructure.Source;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.Gutenberg.Parsing;

public sealed class GutenbergDefinitionParser(ILogger<GutenbergDefinitionParser> logger = null)
    : ISourceDictionaryDefinitionParser
{
    public string SourceCode => "GUT_WEBSTER";

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Definition))
            return Array.Empty<ParsedDefinition>();

        try
        {
            var rawFragment = entry.RawFragmentLine ?? entry.Definition;
            var blocks = ParsingHelperGutenberg.SplitIntoEntryBlocks(rawFragment);

            if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(rawFragment))
                blocks.Add(rawFragment);

            var results = new List<ParsedDefinition>();

            foreach (var block in blocks)
            {
                // Skip foreign headword bleed
                if (ParsingHelperGutenberg.IsForeignHeadwordBlock(block, entry.Word))
                    continue;

                var headword = ParsingHelperGutenberg.ExtractHeadwordFromBlock(block);
                if (string.IsNullOrWhiteSpace(headword))
                    headword = entry.Word;

                if (!ParsingHelperGutenberg.ShouldProcessWord(headword))
                    continue;

                var partOfSpeech = ParsingHelperGutenberg.ExtractPartOfSpeech(block);
                if (string.IsNullOrWhiteSpace(partOfSpeech))
                    partOfSpeech = ParsingHelperGutenberg.ExtractFallbackPartOfSpeech(entry);

                var definitions = ExtractProperDefinitions(block);
                if (definitions == null || definitions.Count == 0)
                    continue;

                var synonyms = ParsingHelperGutenberg.ExtractSynonyms(block);
                var examples = ParsingHelperGutenberg.ExtractExamples(block);
                var etymology = ParsingHelperGutenberg.ExtractEtymology(block);
                var pronunciation = ParsingHelperGutenberg.ExtractPronunciation(block);
                var domains = ParsingHelperGutenberg.ExtractDomains(block);

                var subSenseIndex = 0;

                foreach (var definitionItem in definitions)
                {
                    var (defText, senseNum) = definitionItem;

                    if (string.IsNullOrWhiteSpace(defText))
                        continue;

                    // Skip domain-only or junk definitions
                    if (ParsingHelperGutenberg.IsDomainOnlyDefinition(defText))
                        continue;

                    // Split usage label from definition
                    var (cleanDefinition, usageLabel) =
                        ParsingHelperGutenberg.SplitUsageFromDefinition(defText);

                    if (string.IsNullOrWhiteSpace(cleanDefinition))
                        continue;

                    subSenseIndex++;

                    var finalSenseNum = senseNum > 0 ? senseNum : subSenseIndex;

                    results.Add(new ParsedDefinition
                    {
                        MeaningTitle = string.Empty,
                        Definition = cleanDefinition,
                        RawFragment = block,
                        SenseNumber = finalSenseNum,
                        PartOfSpeech = partOfSpeech,
                        Etymology = etymology,
                        Pronunciation = pronunciation,
                        Domain = domains?.FirstOrDefault(),
                        UsageLabel = usageLabel,
                        CrossReferences = new List<CrossReference>(),
                        Synonyms = synonyms ?? new List<string>(),
                        Alias = null,
                        Examples = examples ?? new List<string>(),
                        DedupKey = ParsingHelperGutenberg.GenerateDedupKey(
                            headword,
                            partOfSpeech,
                            SourceCode)
                    });
                }
            }

            if (results.Count == 0)
            {
                logger?.LogWarning(
                    "No usable definitions extracted from Gutenberg entry: {Word}",
                    entry.Word);
                return Array.Empty<ParsedDefinition>();
            }

            return results;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "Failed to parse Gutenberg entry: {Word}",
                entry.Word);

            return Array.Empty<ParsedDefinition>();
        }
    }

    // Update ExtractProperDefinitions method in GutenbergDefinitionParser.cs
    private List<(string Definition, int SenseNumber)> ExtractProperDefinitions(string block)
    {
        var definitions = new List<(string, int)>();

        if (string.IsNullOrWhiteSpace(block))
            return definitions;

        var lines = ParsingHelperGutenberg.NormalizeNewLines(block)
            .Split('\n')
            .Select(l => l?.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return definitions;

        var currentSense = 1;
        var currentDefinition = new StringBuilder();
        var inDefinition = false;
        var hasEtymologyInSense = false;

        foreach (var line in lines)
        {
            // Skip headword lines
            if (ParsingHelperGutenberg.IsGutenbergHeadwordLine(line, 80))
                continue;

            // Skip pronunciation lines
            if (ParsingHelperGutenberg.RxPronunciationPosLine.IsMatch(line))
                continue;

            // Check for numbered sense start
            var senseMatch = Regex.Match(line, @"^(?<num>\d+)\.\s*(?<content>.*)$");
            if (senseMatch.Success)
            {
                // Save previous definition if any
                if (inDefinition && currentDefinition.Length > 0)
                {
                    var def = CleanDefinitionText(currentDefinition.ToString().Trim());
                    if (IsValidDefinition(def))
                    {
                        definitions.Add((def, currentSense));
                    }
                    currentDefinition.Clear();
                }

                // Parse new sense number
                if (int.TryParse(senseMatch.Groups["num"].Value, out int senseNum))
                {
                    currentSense = senseNum;
                }
                else
                {
                    currentSense++;
                }

                var content = senseMatch.Groups["content"].Value;
                if (!string.IsNullOrWhiteSpace(content))
                {
                    currentDefinition.Append(content);
                    currentDefinition.Append(" ");
                }
                inDefinition = true;
                hasEtymologyInSense = false;
                continue;
            }

            // Check for "Defn:" marker
            if (line.StartsWith("Defn:", StringComparison.OrdinalIgnoreCase))
            {
                // If we're already in a definition, save it
                if (inDefinition && currentDefinition.Length > 0)
                {
                    var def = CleanDefinitionText(currentDefinition.ToString().Trim());
                    if (IsValidDefinition(def))
                    {
                        definitions.Add((def, currentSense));
                    }
                    currentDefinition.Clear();
                }

                var defnContent = line.Substring(5).Trim();
                if (!string.IsNullOrWhiteSpace(defnContent))
                {
                    currentDefinition.Append(defnContent);
                    currentDefinition.Append(" ");
                }
                inDefinition = true;
                continue;
            }

            // Check for "Etym:" marker
            if (line.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase))
            {
                hasEtymologyInSense = true;
                continue;
            }

            // Check for "Syn." marker
            if (line.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Synonyms", StringComparison.OrdinalIgnoreCase))
            {
                if (inDefinition && currentDefinition.Length > 0)
                {
                    var def = CleanDefinitionText(currentDefinition.ToString().Trim());
                    if (IsValidDefinition(def))
                    {
                        definitions.Add((def, currentSense));
                    }
                    currentDefinition.Clear();
                    inDefinition = false;
                }
                continue;
            }

            // If we're in a definition, add continuation lines
            if (inDefinition && !string.IsNullOrWhiteSpace(line))
            {
                // Skip metadata and notes that shouldn't be in definition
                if (ParsingHelperGutenberg.IsMetadataLine(line) ||
                    line.StartsWith("Note:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Obs.", StringComparison.OrdinalIgnoreCase))
                {
                    var cleanedNote = line.Replace("Note:", "").Replace("Note", "").Trim();
                    if (!string.IsNullOrWhiteSpace(cleanedNote))
                    {
                        currentDefinition.Append(cleanedNote);
                        currentDefinition.Append(" ");
                    }
                    continue;
                }

                // Skip domain-only lines
                if (ParsingHelperGutenberg.RxDomainOnlyLine.IsMatch(line))
                    continue;

                // Add normal continuation
                currentDefinition.Append(line);
                currentDefinition.Append(" ");
            }
        }

        // Add the last definition if any
        if (inDefinition && currentDefinition.Length > 0)
        {
            var def = CleanDefinitionText(currentDefinition.ToString().Trim());
            if (IsValidDefinition(def))
            {
                definitions.Add((def, currentSense));
            }
        }

        return definitions;
    }

    // NEW METHOD: Clean definition text by removing examples
    private string CleanDefinitionText(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return definition;

        // Use the new helper method from ParsingHelperGutenberg
        return ParsingHelperGutenberg.RemoveExamplesFromDefinition(definition);
    }

    // NEW METHOD: Remove examples from definitions
    private string CleanDefinitionOfExamples(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return definition;

        var cleaned = definition;

        // Remove quoted examples with author citations
        cleaned = Regex.Replace(cleaned, @"""([^""]+)""\s*\.?\s*[A-Z][a-z]+\.?", "");

        // Remove quoted examples without citations
        cleaned = Regex.Replace(cleaned, @"""([^""]+)""", "");

        // Remove example markers
        cleaned = Regex.Replace(cleaned, @"\bas,\s*", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\be\.g\.,\s*", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bi\.e\.,\s*", "", RegexOptions.IgnoreCase);

        // Clean up extra spaces and punctuation
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        cleaned = cleaned.TrimEnd('.', ',', ';', ':');

        // Ensure proper sentence ending
        if (!string.IsNullOrWhiteSpace(cleaned) &&
            !cleaned.EndsWith(".") &&
            !cleaned.EndsWith("!") &&
            !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    private bool IsValidDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return false;

        var trimmed = definition.Trim();

        if (trimmed.Length < 10)
            return false;

        if (!trimmed.Any(char.IsLetter))
            return false;

        // Skip lines that are just etymology markers
        if (trimmed.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Etymology:", StringComparison.OrdinalIgnoreCase))
            return false;

        // Skip lines that are just synonyms markers
        if (trimmed.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Synonyms", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}