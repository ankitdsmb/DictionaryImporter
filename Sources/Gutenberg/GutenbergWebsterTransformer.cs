using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DictionaryImporter.Sources.Gutenberg;

public sealed class GutenbergWebsterTransformer(ILogger<GutenbergWebsterTransformer> logger)
    : IDataTransformer<GutenbergRawEntry>
{
    private const string SourceCode = "GUT_WEBSTER";

    public IEnumerable<DictionaryEntry> Transform(GutenbergRawEntry raw)
    {
        if (!Helper.ShouldContinueProcessing(SourceCode, logger))
            yield break;

        if (raw == null || string.IsNullOrWhiteSpace(raw.Headword) || raw.Lines == null || raw.Lines.Count == 0)
            yield break;

        logger.LogDebug("Transforming headword {Word}", raw.Headword);

        foreach (var entry in ProcessGutenbergEntry(raw))
            yield return entry;
    }

    private IEnumerable<DictionaryEntry> ProcessGutenbergEntry(GutenbergRawEntry raw)
    {
        var entries = new List<DictionaryEntry>();

        try
        {
            var headword = CleanHeadword(raw.Headword);
            var normalizedWord = Helper.NormalizeWord(headword);
            var rawFragment = string.Join("\n", raw.Lines);

            // Extract part of speech from the headword or first few lines
            var partOfSpeech = ExtractPartOfSpeechFromEntry(raw.Lines, headword);

            var definitions = ExtractDefinitionsFromEntry(raw.Lines, headword);
            if (definitions.Count == 0)
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sense = 1;

            foreach (var def in definitions)
            {
                var cleanedDef = CleanDefinition(def.DefinitionText);
                if (string.IsNullOrWhiteSpace(cleanedDef))
                    continue;

                // Create dedup key from normalized content
                var dedupKey = $"{normalizedWord}|{Helper.NormalizeWord(cleanedDef)}";
                if (!seen.Add(dedupKey))
                {
                    logger.LogDebug("Skipped duplicate definition for {Word}, sense {Sense}", headword, sense);
                    continue;
                }

                entries.Add(new DictionaryEntry
                {
                    Word = headword,
                    NormalizedWord = normalizedWord,
                    Definition = cleanedDef,
                    RawFragment = rawFragment,
                    SenseNumber = def.SenseNumber > 0 ? def.SenseNumber : sense,
                    SourceCode = SourceCode,
                    PartOfSpeech = partOfSpeech,
                    CreatedUtc = DateTime.UtcNow
                });

                sense++;
            }

            Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));
        }
        catch (Exception ex)
        {
            Helper.HandleError(logger, ex, SourceCode, "transforming");
        }

        foreach (var entry in entries)
            yield return entry;
    }

    private string CleanHeadword(string headword)
    {
        if (string.IsNullOrWhiteSpace(headword))
            return string.Empty;

        var cleaned = headword.Trim();

        // Remove parentheticals like "(# emph. #)"
        if (cleaned.Contains('(') && cleaned.Contains(')'))
        {
            var start = cleaned.IndexOf('(');
            var end = cleaned.LastIndexOf(')');
            if (end > start)
            {
                cleaned = cleaned.Remove(start, end - start + 1).Trim();
            }
        }

        // Remove trailing punctuation
        cleaned = cleaned.TrimEnd('.', ',', ';', ':');

        // Extract just the actual word (first part before any special characters)
        var parts = cleaned.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            // Take the first part that looks like a word
            foreach (var part in parts)
            {
                if (part.Length > 0 && char.IsLetter(part[0]))
                {
                    return part;
                }
            }
        }

        return cleaned;
    }

    private string ExtractPartOfSpeechFromEntry(List<string> lines, string headword)
    {
        if (lines == null || lines.Count == 0)
            return "noun";

        // Look for POS patterns in the entire block
        var text = string.Join(" ", lines.Take(10));

        // Check for common POS patterns
        if (Regex.IsMatch(text, @"\b(noun|n\.)\b", RegexOptions.IgnoreCase))
            return "noun";
        if (Regex.IsMatch(text, @"\b(verb|v\.|v\.t\.|v\.i\.)\b", RegexOptions.IgnoreCase))
            return "verb";
        if (Regex.IsMatch(text, @"\b(adjective|adj\.|a\.)\b", RegexOptions.IgnoreCase))
            return "adjective";
        if (Regex.IsMatch(text, @"\b(adverb|adv\.)\b", RegexOptions.IgnoreCase))
            return "adverb";
        if (Regex.IsMatch(text, @"\b(preposition|prep\.)\b", RegexOptions.IgnoreCase))
            return "preposition";
        if (Regex.IsMatch(text, @"\b(conjunction|conj\.)\b", RegexOptions.IgnoreCase))
            return "conjunction";
        if (Regex.IsMatch(text, @"\b(interjection|interj\.)\b", RegexOptions.IgnoreCase))
            return "interjection";
        if (Regex.IsMatch(text, @"\b(pronoun|pron\.)\b", RegexOptions.IgnoreCase))
            return "pronoun";

        // Check headword for POS (e.g., "A, prep.")
        if (headword.Contains(','))
        {
            var parts = headword.Split(',');
            if (parts.Length > 1)
            {
                var posPart = parts[1].Trim().TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(posPart) && posPart.Length < 10)
                {
                    return ParsingHelperGutenberg.NormalizePartOfSpeech(posPart);
                }
            }
        }

        return "noun"; // Default fallback
    }

    private List<DefinitionItem> ExtractDefinitionsFromEntry(List<string> lines, string headword)
    {
        var definitions = new List<DefinitionItem>();

        if (lines == null || lines.Count == 0)
            return definitions;

        var currentSense = 1;
        var currentDefinition = new StringBuilder();
        var inDefinition = false;
        var collectingExamples = false;
        var hasEtymology = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Skip headword line
            if (ParsingHelperGutenberg.IsGutenbergHeadwordLine(line, 80))
                continue;

            // Check for etymology marker - don't include in definition
            if (line.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase))
            {
                hasEtymology = true;
                // End current definition if we're in one
                if (inDefinition && currentDefinition.Length > 0)
                {
                    var def = currentDefinition.ToString().Trim();
                    if (IsValidDefinitionText(def))
                    {
                        definitions.Add(new DefinitionItem(def, currentSense));
                    }
                    currentDefinition.Clear();
                    inDefinition = false;
                }
                continue;
            }

            // Check for numbered sense
            var senseMatch = Regex.Match(line, @"^(?<num>\d+)\.\s*\(?(?<content>.*)$");
            if (senseMatch.Success)
            {
                // Save previous definition
                if (inDefinition && currentDefinition.Length > 0)
                {
                    var def = currentDefinition.ToString().Trim();
                    if (IsValidDefinitionText(def))
                    {
                        definitions.Add(new DefinitionItem(def, currentSense));
                    }
                    currentDefinition.Clear();
                }

                // Parse sense number
                if (int.TryParse(senseMatch.Groups["num"].Value, out int senseNum))
                {
                    currentSense = senseNum;
                }

                var content = senseMatch.Groups["content"].Value.Trim();
                // Remove domain in parentheses if present
                if (content.StartsWith("(") && content.Contains(")"))
                {
                    var endParen = content.IndexOf(')');
                    if (endParen > 0)
                    {
                        content = content.Substring(endParen + 1).Trim();
                    }
                }

                if (!string.IsNullOrWhiteSpace(content))
                {
                    currentDefinition.Append(content);
                    currentDefinition.Append(" ");
                }
                inDefinition = true;
                collectingExamples = false;
                hasEtymology = false;
                continue;
            }

            // Check for "Defn:" marker
            if (line.StartsWith("Defn:", StringComparison.OrdinalIgnoreCase))
            {
                // Save previous definition
                if (inDefinition && currentDefinition.Length > 0)
                {
                    var def = currentDefinition.ToString().Trim();
                    if (IsValidDefinitionText(def))
                    {
                        definitions.Add(new DefinitionItem(def, currentSense));
                    }
                    currentDefinition.Clear();
                }

                var content = line.Substring(5).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    currentDefinition.Append(content);
                    currentDefinition.Append(" ");
                }
                inDefinition = true;
                collectingExamples = false;
                hasEtymology = false;
                continue;
            }

            // Check for synonym markers - end definition
            if (line.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Synonyms", StringComparison.OrdinalIgnoreCase))
            {
                if (inDefinition && currentDefinition.Length > 0)
                {
                    var def = currentDefinition.ToString().Trim();
                    if (IsValidDefinitionText(def))
                    {
                        definitions.Add(new DefinitionItem(def, currentSense));
                    }
                    currentDefinition.Clear();
                    inDefinition = false;
                }
                continue;
            }

            // Check for example markers
            if (line.StartsWith("--"))
            {
                if (inDefinition && currentDefinition.Length > 0)
                {
                    collectingExamples = true;
                    // Don't add example markers to definition
                    continue;
                }
            }

            // Add to current definition if we're in one
            if (inDefinition && !collectingExamples)
            {
                // Skip metadata lines
                if (ParsingHelperGutenberg.IsMetadataLine(line) ||
                    ParsingHelperGutenberg.RxDomainOnlyLine.IsMatch(line))
                    continue;

                currentDefinition.Append(line);
                currentDefinition.Append(" ");
            }
        }

        // Add the last definition
        if (inDefinition && currentDefinition.Length > 0)
        {
            var def = currentDefinition.ToString().Trim();
            if (IsValidDefinitionText(def))
            {
                definitions.Add(new DefinitionItem(def, currentSense));
            }
        }

        return definitions;
    }

    private bool IsValidDefinitionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();

        if (trimmed.Length < 10)
            return false;

        if (!trimmed.Any(char.IsLetter))
            return false;

        // Skip etymology-only lines
        if (trimmed.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("[Etym:") || trimmed.Contains("Etym: ["))
            return false;

        // Skip synonym-only lines
        if (trimmed.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Synonyms", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private string CleanDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var cleaned = definition.Trim();

        // Remove "Defn:" markers
        cleaned = cleaned.Replace("Defn:", "").Replace("defn:", "").Trim();

        // Clean up multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ");

        // Ensure proper punctuation
        if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    private class DefinitionItem
    {
        public string DefinitionText { get; }
        public int SenseNumber { get; }

        public DefinitionItem(string definitionText, int senseNumber)
        {
            DefinitionText = definitionText;
            SenseNumber = senseNumber;
        }
    }
}