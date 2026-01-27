using DictionaryImporter.Common;
using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordTransformer(ILogger<OxfordTransformer> logger)
: IDataTransformer<OxfordRawEntry>
{
    private const string SourceCode = "ENG_OXFORD";

    public IEnumerable<DictionaryEntry> Transform(OxfordRawEntry? raw)
    {
        if (raw == null || raw.Senses == null || raw.Senses.Count == 0)
            yield break;

        foreach (var entry in ProcessOxfordEntry(raw))
        {
            // apply limit per produced DictionaryEntry
            if (!Helper.ShouldContinueProcessing(SourceCode, logger))
                yield break;

            yield return entry;
        }
    }

    private IEnumerable<DictionaryEntry> ProcessOxfordEntry(OxfordRawEntry raw)
    {
        var entries = new List<DictionaryEntry>();

        try
        {
            var normalizedWord =
                Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

            foreach (var sense in raw.Senses)
            {
                // 1. POS resolution (Oxford-correct)
                var resolvedPos = ResolvePartOfSpeech(raw, sense);

                // 2. Build full definition with proper section ordering
                // DO NOT clean structured sections - let the parser handle them
                var fullDefinition = BuildFullDefinition(raw, sense);

                // 3. Extract etymology text if present
                var etymology = ExtractEtymology(fullDefinition);

                // 4. Create entry - keep structured sections in definition
                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = resolvedPos,
                    Definition = fullDefinition, // Keep structured sections for parser
                    Etymology = etymology,
                    RawFragment = sense.Definition, // Keep sense-level raw fragment
                    SenseNumber = sense.SenseNumber,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow
                });
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

    private static string ResolvePartOfSpeech(OxfordRawEntry raw, OxfordSenseRaw sense)
    {
        // Priority 1: Sense-level POS block (▶ adjective, noun, etc.)
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            // Check for explicit POS markers in sense label
            var posMatch = Regex.Match(sense.SenseLabel, @"▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol)", RegexOptions.IgnoreCase);
            if (posMatch.Success)
            {
                var pos = posMatch.Groups[1].Value.Trim();
                var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(pos);
                if (normalized != "unk")
                    return normalized;
            }

            // Check if sense label is a POS itself
            var normalizedFromLabel = OxfordSourceDataHelper.NormalizePartOfSpeech(sense.SenseLabel);
            if (normalizedFromLabel != "unk")
                return normalizedFromLabel;
        }

        // Priority 2: Check definition for POS markers at beginning
        if (!string.IsNullOrWhiteSpace(sense.Definition))
        {
            // Look for ▶ POS marker at start of definition
            var posMarkerMatch = Regex.Match(sense.Definition, @"^▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol)\b", RegexOptions.IgnoreCase);
            if (posMarkerMatch.Success)
            {
                var pos = posMarkerMatch.Groups[1].Value.Trim();
                var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(pos);
                if (normalized != "unk")
                    return normalized;
            }

            // Check for POS in parentheses at beginning
            var firstParenMatch = Regex.Match(sense.Definition, @"^\(([^)]+)\)");
            if (firstParenMatch.Success)
            {
                var possiblePos = firstParenMatch.Groups[1].Value.Trim();
                // Filter out non-POS labels
                if (!possiblePos.Contains("[") && !possiblePos.Contains("]") &&
                    !possiblePos.Contains("informal") && !possiblePos.Contains("formal") &&
                    !possiblePos.Contains("dated") && !possiblePos.Contains("archaic"))
                {
                    var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(possiblePos);
                    if (normalized != "unk")
                        return normalized;
                }
            }
        }

        // Priority 3: Headword-level POS
        if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
        {
            var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech);
            if (normalized != "unk")
                return normalized;
        }

        // Default: unknown
        return "unk";
    }

    private static string BuildFullDefinition(OxfordRawEntry entry, OxfordSenseRaw sense)
    {
        var parts = new List<string>();

        // Pronunciation (if available at entry level)
        if (!string.IsNullOrWhiteSpace(entry.Pronunciation))
        {
            var cleanPron = entry.Pronunciation.Trim();
            // Remove any Chinese text from pronunciation
            cleanPron = Regex.Replace(cleanPron, @"[\u4e00-\u9fff].*$", "").Trim();
            if (!string.IsNullOrWhiteSpace(cleanPron))
                parts.Add($"【Pronunciation】{cleanPron}");
        }

        // Variants (if available at entry level)
        if (!string.IsNullOrWhiteSpace(entry.VariantForms))
        {
            var cleanVariants = entry.VariantForms.Trim();
            // Remove Chinese text
            cleanVariants = Regex.Replace(cleanVariants, @"[\u4e00-\u9fff].*$", "").Trim();
            if (!string.IsNullOrWhiteSpace(cleanVariants))
                parts.Add($"【Variants】{cleanVariants}");
        }

        // Sense label (domain/register information)
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            var cleanLabel = sense.SenseLabel;
            // Remove Chinese text
            cleanLabel = Regex.Replace(cleanLabel, @"[\u4e00-\u9fff].*$", "").Trim();
            // Remove POS markers that we've already extracted
            cleanLabel = Regex.Replace(cleanLabel, @"▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol)\b", "", RegexOptions.IgnoreCase).Trim();

            if (!string.IsNullOrWhiteSpace(cleanLabel))
                parts.Add($"【Label】{cleanLabel}");
        }

        // Main definition - Extract English only (before Chinese bullet)
        var mainDefinition = sense.Definition ?? string.Empty;

        // Remove Chinese translation (text after • that contains Chinese characters)
        mainDefinition = Regex.Replace(mainDefinition, @"•\s*[\u4e00-\u9fff].*$", "").Trim();

        // Remove any remaining Chinese text
        mainDefinition = Regex.Replace(mainDefinition, @"[\u4e00-\u9fff]", "").Trim();

        // Clean up formatting artifacts but KEEP structured section markers
        mainDefinition = Regex.Replace(mainDefinition, @"\s+", " ").Trim();

        if (!string.IsNullOrWhiteSpace(mainDefinition))
            parts.Add(mainDefinition);

        // Examples - Only English examples
        if (sense.Examples != null && sense.Examples.Count > 0)
        {
            var englishExamples = new List<string>();
            foreach (var example in sense.Examples)
            {
                var cleanExample = example;
                // Remove Chinese text from examples
                cleanExample = Regex.Replace(cleanExample, @"[\u4e00-\u9fff].*$", "").Trim();
                // Remove any remaining Chinese characters
                cleanExample = Regex.Replace(cleanExample, @"[\u4e00-\u9fff]", "").Trim();
                // Remove example markers
                cleanExample = cleanExample.TrimStart('»', ' ').Trim();

                if (!string.IsNullOrWhiteSpace(cleanExample) && cleanExample.Length > 5)
                    englishExamples.Add(cleanExample);
            }

            if (englishExamples.Count > 0)
            {
                parts.Add("【Examples】");
                foreach (var example in englishExamples)
                    parts.Add($"» {example}");
            }
        }

        // Usage note - Clean Chinese text
        if (!string.IsNullOrWhiteSpace(sense.UsageNote))
        {
            var cleanUsage = sense.UsageNote;
            // Remove Chinese text
            cleanUsage = Regex.Replace(cleanUsage, @"[\u4e00-\u9fff].*$", "").Trim();
            cleanUsage = Regex.Replace(cleanUsage, @"[\u4e00-\u9fff]", "").Trim();

            if (!string.IsNullOrWhiteSpace(cleanUsage))
            {
                if (cleanUsage.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
                    parts.Add($"【Usage】{cleanUsage.Substring(6).Trim()}");
                else if (cleanUsage.StartsWith("Grammar:", StringComparison.OrdinalIgnoreCase))
                    parts.Add($"【Grammar】{cleanUsage.Substring(8).Trim()}");
                else if (cleanUsage.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
                    parts.Add($"【Note】{cleanUsage.Substring(5).Trim()}");
                else
                    parts.Add($"【Usage】{cleanUsage}");
            }
        }

        // Cross-references - Extract from definition
        if (!string.IsNullOrWhiteSpace(sense.Definition))
        {
            var crossRefs = ExtractCrossReferences(sense.Definition);
            if (crossRefs.Count > 0)
            {
                parts.Add($"【SeeAlso】{string.Join("; ", crossRefs)}");
            }
        }

        return string.Join("\n", parts);
    }

    private static List<string> ExtractCrossReferences(string definition)
    {
        var crossRefs = new List<string>();

        if (string.IsNullOrWhiteSpace(definition))
            return crossRefs;

        // Pattern 1: --› see word
        var seeMatches = Regex.Matches(definition, @"--›\s*(?:see|cf\.?|compare)\s+([A-Za-z\-']+)");
        foreach (Match match in seeMatches)
        {
            if (match.Groups[1].Success)
                crossRefs.Add(match.Groups[1].Value.Trim());
        }

        // Pattern 2: variant of word
        var variantMatches = Regex.Matches(definition, @"(?:variant of|another term for|同)\s+([A-Za-z\-']+)");
        foreach (Match match in variantMatches)
        {
            if (match.Groups[1].Success)
                crossRefs.Add(match.Groups[1].Value.Trim());
        }

        // Pattern 3: (also word)
        var alsoMatches = Regex.Matches(definition, @"\(also\s+([A-Za-z\-']+)\)");
        foreach (Match match in alsoMatches)
        {
            if (match.Groups[1].Success)
                crossRefs.Add(match.Groups[1].Value.Trim());
        }

        return crossRefs.Distinct().ToList();
    }

    private static string? ExtractEtymology(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // Look for etymology section
        var etymologyMatch = Regex.Match(definition, @"【语源】\s*(.+?)(?:\n【|$)");
        if (etymologyMatch.Success)
        {
            var etymology = etymologyMatch.Groups[1].Value.Trim();
            // Clean Chinese text from etymology
            etymology = Regex.Replace(etymology, @"[\u4e00-\u9fff]", "").Trim();
            return !string.IsNullOrWhiteSpace(etymology) ? etymology : null;
        }

        return null;
    }

    // This method was removed from OxfordTransformer because it shouldn't clean the definition here
    // The cleaning should happen in the parser (OxfordDefinitionParser)
}