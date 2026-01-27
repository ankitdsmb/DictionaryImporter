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

                // 2. Extract etymology text if present
                var etymology = ExtractEtymology(sense);

                // 3. Build clean definition (English only)
                var cleanDefinition = BuildCleanDefinition(sense);

                // 4. Build structured definition with sections
                var structuredDefinition = BuildStructuredDefinition(raw, sense, cleanDefinition);

                // 5. Clean the RawFragment (remove Chinese, keep English)
                var cleanRawFragment = CleanRawFragment(sense.Definition);

                // 6. Create entry
                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = resolvedPos,
                    Definition = structuredDefinition,
                    Etymology = etymology,
                    RawFragment = cleanRawFragment,
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

    private static string BuildCleanDefinition(OxfordSenseRaw sense)
    {
        if (string.IsNullOrWhiteSpace(sense.Definition))
            return string.Empty;

        var definition = sense.Definition;

        // 1. Remove Chinese text and translations
        definition = RemoveChineseText(definition);

        // 2. Remove bullet markers and Chinese markers
        definition = Regex.Replace(definition, @"•\s*\[.*?\]", ""); // Remove • [text]
        definition = Regex.Replace(definition, @"•", ""); // Remove remaining bullets

        // 3. Remove formatting artifacts
        definition = Regex.Replace(definition, @"▶", "");
        definition = Regex.Replace(definition, @"◘", "");
        definition = Regex.Replace(definition, @"--›", "");
        definition = Regex.Replace(definition, @"»", "");
        definition = Regex.Replace(definition, @"♦", "");

        // 4. Remove section markers that might be in the middle of text
        definition = Regex.Replace(definition, @"【[^】]*】", "");

        // 5. Clean up whitespace
        definition = Regex.Replace(definition, @"\s+", " ").Trim();

        // 6. Remove trailing punctuation without content
        definition = definition.TrimEnd('.', ',', ';', ':', '!', '?', '•');

        return definition;
    }

    private static string RemoveChineseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters
        text = Regex.Replace(text, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        text = Regex.Replace(text, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        return text;
    }

    private static string BuildStructuredDefinition(OxfordRawEntry entry, OxfordSenseRaw sense, string cleanDefinition)
    {
        var parts = new List<string>();

        // Add Label section if we have sense label info
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            var cleanLabel = RemoveChineseText(sense.SenseLabel);
            cleanLabel = Regex.Replace(cleanLabel, @"▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol)\b", "", RegexOptions.IgnoreCase).Trim();

            if (!string.IsNullOrWhiteSpace(cleanLabel) && cleanLabel != "unk")
                parts.Add($"【Label】{cleanLabel}");
        }

        // Add the clean definition
        if (!string.IsNullOrWhiteSpace(cleanDefinition))
            parts.Add(cleanDefinition);

        // Add Examples section if we have English examples
        if (sense.Examples != null && sense.Examples.Count > 0)
        {
            var englishExamples = new List<string>();
            foreach (var example in sense.Examples)
            {
                var cleanExample = RemoveChineseText(example);
                cleanExample = cleanExample.TrimStart('»', ' ').Trim();
                cleanExample = Regex.Replace(cleanExample, @"\s+", " ").Trim();

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

        return string.Join("\n", parts);
    }

    private static string? ExtractEtymology(OxfordSenseRaw sense)
    {
        if (string.IsNullOrWhiteSpace(sense.Definition))
            return null;

        // Look for etymology section in the definition
        var etymologyMatch = Regex.Match(sense.Definition, @"【语源】\s*(.+?)(?:\n【|$)");
        if (etymologyMatch.Success)
        {
            var etymology = etymologyMatch.Groups[1].Value.Trim();
            // Clean Chinese text from etymology
            etymology = RemoveChineseText(etymology);
            return !string.IsNullOrWhiteSpace(etymology) ? etymology : null;
        }

        return null;
    }

    private static string CleanRawFragment(string? rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        // Remove Chinese text from raw fragment
        var cleanFragment = RemoveChineseText(rawFragment);

        // Remove Chinese bullet markers
        cleanFragment = Regex.Replace(cleanFragment, @"•\s*\[.*?\]", "");
        cleanFragment = Regex.Replace(cleanFragment, @"•", "");

        // Clean up whitespace
        cleanFragment = Regex.Replace(cleanFragment, @"\s+", " ").Trim();

        return cleanFragment;
    }
}