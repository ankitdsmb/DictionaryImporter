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

        // 3. Remove Chinese text and markers - BUT KEEP ENGLISH CONTENT
        definition = RemoveChineseContentButKeepEnglish(definition);

        // 4. Clean up formatting
        definition = CleanFormatting(definition);

        // 5. Process RawFragment similarly but keep more content
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
            // Clean Chinese text but keep English etymology
            etymology = RemoveChineseContentButKeepEnglish(etymology);
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

    private static string RemoveChineseContentButKeepEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters and their translations
        // Pattern: English text • Chinese translation
        text = Regex.Replace(text, @"•\s*[\u4e00-\u9fff].*?(?=\n|$)", "", RegexOptions.Multiline);

        // Remove standalone Chinese characters
        text = Regex.Replace(text, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        text = Regex.Replace(text, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        // Remove Chinese section markers but keep content
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

        // Remove formatting artifacts but keep meaningful content
        text = Regex.Replace(text, @"[▶»◘›♦]", " ");

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

        // Remove Chinese text but keep English
        fragment = RemoveChineseContentButKeepEnglish(fragment);

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
            var cleanLabel = RemoveChineseContentButKeepEnglish(sense.SenseLabel);
            cleanLabel = CleanFormatting(cleanLabel);

            // Extract domain/usage labels from sense label
            var domainLabels = ExtractDomainAndUsageLabels(cleanLabel);
            if (!string.IsNullOrWhiteSpace(domainLabels))
            {
                parts.Add($"【Label】{domainLabels}");
            }

            // Remove POS markers from label for cleaner display
            cleanLabel = Regex.Replace(cleanLabel, @"▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol)\b", "", RegexOptions.IgnoreCase).Trim();

            if (!string.IsNullOrWhiteSpace(cleanLabel) && cleanLabel != "unk")
            {
                if (string.IsNullOrWhiteSpace(domainLabels))
                    parts.Add($"【Label】{cleanLabel}");
            }
        }

        // Add the clean definition - CRITICAL: Ensure this is NOT empty
        if (!string.IsNullOrWhiteSpace(cleanDefinition))
        {
            // Extract main definition (text before Examples section)
            var mainDefinition = ExtractMainDefinitionBeforeExamples(cleanDefinition);
            if (!string.IsNullOrWhiteSpace(mainDefinition))
                parts.Add(mainDefinition);
            else
                parts.Add(cleanDefinition); // Fallback to full clean definition
        }

        // Add Examples section if we have English examples
        if (sense.Examples != null && sense.Examples.Count > 0)
        {
            var englishExamples = new List<string>();
            foreach (var example in sense.Examples)
            {
                var cleanExample = RemoveChineseContentButKeepEnglish(example);
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

    private static string ExtractMainDefinitionBeforeExamples(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return definition;

        // Find the 【Examples】 section
        var examplesIndex = definition.IndexOf("【Examples】", StringComparison.Ordinal);
        if (examplesIndex > 0)
        {
            return definition.Substring(0, examplesIndex).Trim();
        }

        // If no examples section, return the whole definition
        return definition;
    }

    private static string ExtractDomainAndUsageLabels(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        var domainLabels = new List<string>();

        // Extract common domain/usage labels
        var labelLower = label.ToLowerInvariant();

        // Check for common usage labels
        if (labelLower.Contains("informal")) domainLabels.Add("[informal]");
        if (labelLower.Contains("formal")) domainLabels.Add("[formal]");
        if (labelLower.Contains("dated")) domainLabels.Add("[dated]");
        if (labelLower.Contains("archaic")) domainLabels.Add("[archaic]");
        if (labelLower.Contains("slang")) domainLabels.Add("[slang]");
        if (labelLower.Contains("technical")) domainLabels.Add("[technical]");
        if (labelLower.Contains("literary")) domainLabels.Add("[literary]");
        if (labelLower.Contains("humorous")) domainLabels.Add("[humorous]");

        // Check for regional labels
        if (labelLower.Contains("north american") || labelLower.Contains("n. amer."))
            domainLabels.Add("[N. Amer.]");
        if (labelLower.Contains("british")) domainLabels.Add("[British]");

        return string.Join(", ", domainLabels);
    }

    private static string ResolvePartOfSpeech(OxfordRawEntry raw, OxfordSenseRaw sense)
    {
        // Priority 1: Sense-level POS block (▶ adjective, noun, etc.)
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            // Check for explicit POS markers in sense label
            var posMatch = Regex.Match(sense.SenseLabel, @"▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol|combining form|phrasal verb|idiom)", RegexOptions.IgnoreCase);
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
            var posMarkerMatch = Regex.Match(sense.Definition, @"^▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol|combining form|phrasal verb|idiom)\b", RegexOptions.IgnoreCase);
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
                // Filter out non-POS labels but allow known POS values
                var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(possiblePos);
                if (normalized != "unk")
                    return normalized;
            }
        }

        // Priority 3: Headword-level POS
        if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
        {
            var normalized = OxfordSourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech);
            if (normalized != "unk")
                return normalized;
        }

        // Priority 4: Check for common suffixes in headword
        if (!string.IsNullOrWhiteSpace(raw.Headword))
        {
            var headword = raw.Headword.ToLowerInvariant();
            if (headword.EndsWith("ness") || headword.EndsWith("ment") || headword.EndsWith("tion") || headword.EndsWith("sion"))
                return "noun";
            if (headword.EndsWith("able") || headword.EndsWith("ible") || headword.EndsWith("ive") || headword.EndsWith("ous"))
                return "adj";
            if (headword.EndsWith("ly"))
                return "adv";
            if (headword.EndsWith("ize") || headword.EndsWith("ise"))
                return "verb";
            if (headword.EndsWith("ify"))
                return "verb";
            if (headword.StartsWith("un") || headword.StartsWith("re") || headword.StartsWith("dis") || headword.StartsWith("mis"))
                return "prefix";
        }

        // Default: unknown
        return "unk";
    }
}