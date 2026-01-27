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

                // 2. Build clean definition and extract etymology separately
                var (cleanDefinition, etymology, cleanRawFragment) = ProcessSenseDefinition(sense);

                // 3. Build structured definition with proper sections
                var structuredDefinition = BuildStructuredDefinition(raw, sense, cleanDefinition);

                // 4. Create entry
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

    private static (string cleanDefinition, string? etymology, string cleanRawFragment) ProcessSenseDefinition(OxfordSenseRaw sense)
    {
        if (string.IsNullOrWhiteSpace(sense.Definition))
            return (string.Empty, null, string.Empty);

        var definition = sense.Definition;
        string? etymology = null;

        // 1. Extract etymology FIRST before cleaning
        etymology = ExtractEtymologyText(definition);

        // 2. Remove etymology section from definition
        if (!string.IsNullOrEmpty(etymology))
        {
            definition = RemoveEtymologySection(definition);
        }

        // 3. Remove Chinese text and markers
        definition = RemoveAllChineseContent(definition);

        // 4. Remove square brackets that contain ONLY English words (these are Chinese translation markers)
        definition = RemoveChineseTranslationMarkers(definition);

        // 5. Clean up formatting
        definition = CleanFormatting(definition);

        // 6. Process RawFragment similarly but keep more content
        var cleanRawFragment = CleanRawFragment(sense.Definition);

        return (definition.Trim(), etymology, cleanRawFragment);
    }

    private static string? ExtractEtymologyText(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // Look for 【语源】 section
        var etymologyMatch = Regex.Match(definition, @"【语源】\s*(.+?)(?:\n【|$)");
        if (etymologyMatch.Success)
        {
            var etymology = etymologyMatch.Groups[1].Value.Trim();
            // Clean Chinese text
            etymology = RemoveAllChineseContent(etymology);
            etymology = RemoveChineseTranslationMarkers(etymology);
            etymology = CleanFormatting(etymology);

            return !string.IsNullOrWhiteSpace(etymology) ? etymology : null;
        }

        return null;
    }

    private static string RemoveEtymologySection(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return definition;

        // Remove the entire 【语源】 section
        return Regex.Replace(definition, @"【语源】[^\n]*(\n[^\n【]*)*", "");
    }

    private static string RemoveAllChineseContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters
        text = Regex.Replace(text, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        text = Regex.Replace(text, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        // Remove Chinese section markers
        text = Regex.Replace(text, @"【[^】]*】", " ");

        return text;
    }

    private static string RemoveChineseTranslationMarkers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove markers like [of], [have], [to] - these are Chinese translation markers
        // Only remove if they contain a single English word
        text = Regex.Replace(text, @"\[([A-Za-z]+)\]", "$1");

        // Remove empty brackets []
        text = Regex.Replace(text, @"\[\s*\]", "");

        // Remove any remaining brackets with content (might be other markers)
        text = Regex.Replace(text, @"\[[^\]]*\]", "");

        return text;
    }

    private static string CleanFormatting(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove bullet markers
        text = Regex.Replace(text, @"•", " ");

        // Remove formatting artifacts
        text = Regex.Replace(text, @"[▶»◘›♦\-]", " ");

        // Remove example markers at start of lines
        text = Regex.Replace(text, @"^»\s*", "", RegexOptions.Multiline);

        // Clean up whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Remove trailing punctuation
        text = text.TrimEnd('.', ',', ';', ':', '!', '?', '•');

        return text;
    }

    private static string CleanRawFragment(string? rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var fragment = rawFragment;

        // Remove Chinese text
        fragment = RemoveAllChineseContent(fragment);

        // Remove Chinese translation markers
        fragment = RemoveChineseTranslationMarkers(fragment);

        // Clean formatting but keep more structure
        fragment = Regex.Replace(fragment, @"•", " ");
        fragment = Regex.Replace(fragment, @"\s+", " ").Trim();

        return fragment;
    }

    private static string BuildStructuredDefinition(OxfordRawEntry entry, OxfordSenseRaw sense, string cleanDefinition)
    {
        var parts = new List<string>();

        // Add Label section if we have sense label info
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            var cleanLabel = RemoveAllChineseContent(sense.SenseLabel);
            cleanLabel = RemoveChineseTranslationMarkers(cleanLabel);
            cleanLabel = CleanFormatting(cleanLabel);

            // Remove POS markers from label
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
                var cleanExample = RemoveAllChineseContent(example);
                cleanExample = RemoveChineseTranslationMarkers(cleanExample);
                cleanExample = CleanFormatting(cleanExample);
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

        return string.Join("\n", parts);
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
}