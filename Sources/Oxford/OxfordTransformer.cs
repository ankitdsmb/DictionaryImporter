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

            // Track sense numbers properly
            int senseCounter = 1;

            foreach (var sense in raw.Senses)
            {
                // 1. POS resolution with confidence
                var (resolvedPos, posConfidence) = ResolvePartOfSpeechWithConfidence(raw, sense);

                // 2. Extract clean English definition
                var (cleanDefinition, domainLabels, usageLabels) = ExtractCleanEnglishDefinition(sense);

                // 3. Extract etymology (cleanly, without sense numbers)
                var etymology = ExtractCleanEtymology(sense.Definition);

                // 4. Preserve RawFragment with original content (but clean Chinese)
                var cleanRawFragment = PreserveRawFragment(sense.Definition);

                // 5. Build structured definition WITHOUT domain labels in definition text
                var structuredDefinition = BuildCleanStructuredDefinition(
                    raw, sense, cleanDefinition, domainLabels, usageLabels);

                // 6. Ensure sense number is proper (1-based)
                var properSenseNumber = sense.SenseNumber > 0 ? sense.SenseNumber : senseCounter;

                entries.Add(new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = resolvedPos,
                    PartOfSpeechConfidence = posConfidence,
                    Definition = structuredDefinition,
                    Etymology = etymology,
                    SenseNumber = properSenseNumber,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow,
                    RawFragment = cleanRawFragment
                });

                senseCounter++;
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

    private static (string resolvedPos, int? confidence) ResolvePartOfSpeechWithConfidence(OxfordRawEntry raw, OxfordSenseRaw sense)
    {
        var confidence = 0;
        string resolvedPos = "unk";

        // Priority 1: Explicit POS marker with high confidence
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            var posMatch = Regex.Match(sense.SenseLabel, @"▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol|combining form|phrasal verb|idiom)", RegexOptions.IgnoreCase);
            if (posMatch.Success)
            {
                resolvedPos = OxfordSourceDataHelper.NormalizePartOfSpeech(posMatch.Groups[1].Value);
                confidence = 95; // High confidence for explicit markers
                return (resolvedPos, confidence);
            }

            // Check if sense label itself is a POS
            var normalizedFromLabel = OxfordSourceDataHelper.NormalizePartOfSpeech(sense.SenseLabel);
            if (normalizedFromLabel != "unk")
            {
                resolvedPos = normalizedFromLabel;
                confidence = 85;
                return (resolvedPos, confidence);
            }
        }

        // Priority 2: Check definition for POS markers
        if (!string.IsNullOrWhiteSpace(sense.Definition))
        {
            var posMarkerMatch = Regex.Match(sense.Definition, @"^▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol|combining form|phrasal verb|idiom)\b", RegexOptions.IgnoreCase);
            if (posMarkerMatch.Success)
            {
                resolvedPos = OxfordSourceDataHelper.NormalizePartOfSpeech(posMarkerMatch.Groups[1].Value);
                confidence = 90;
                return (resolvedPos, confidence);
            }
        }

        // Priority 3: Headword-level POS
        if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
        {
            resolvedPos = OxfordSourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech);
            confidence = 80;
            return (resolvedPos, confidence);
        }

        // Priority 4: Infer from headword suffixes/prefixes
        if (!string.IsNullOrWhiteSpace(raw.Headword))
        {
            var inferred = InferPosFromHeadword(raw.Headword);
            if (inferred != "unk")
            {
                resolvedPos = inferred;
                confidence = 60; // Medium confidence for inference
                return (resolvedPos, confidence);
            }
        }

        // Default
        return ("unk", 0);
    }

    private static string InferPosFromHeadword(string headword)
    {
        var hw = headword.ToLowerInvariant();

        // Noun suffixes
        if (hw.EndsWith("ness") || hw.EndsWith("ment") || hw.EndsWith("tion") ||
            hw.EndsWith("sion") || hw.EndsWith("ity") || hw.EndsWith("ance") ||
            hw.EndsWith("ence") || hw.EndsWith("hood") || hw.EndsWith("ship"))
            return "noun";

        // Adjective suffixes
        if (hw.EndsWith("able") || hw.EndsWith("ible") || hw.EndsWith("ive") ||
            hw.EndsWith("ous") || hw.EndsWith("ful") || hw.EndsWith("less") ||
            hw.EndsWith("ish") || hw.EndsWith("ic") || hw.EndsWith("al"))
            return "adj";

        // Adverb suffix
        if (hw.EndsWith("ly"))
            return "adv";

        // Verb suffixes
        if (hw.EndsWith("ize") || hw.EndsWith("ise") || hw.EndsWith("ify") ||
            hw.EndsWith("ate") || hw.EndsWith("en"))
            return "verb";

        // Prefix detection
        if (hw.StartsWith("un") || hw.StartsWith("re") || hw.StartsWith("dis") ||
            hw.StartsWith("mis") || hw.StartsWith("pre") || hw.StartsWith("post") ||
            hw.StartsWith("over") || hw.StartsWith("under") || hw.StartsWith("sub"))
            return "prefix";

        return "unk";
    }

    private static (string cleanDefinition, string? domain, string? usage) ExtractCleanEnglishDefinition(OxfordSenseRaw sense)
    {
        if (string.IsNullOrWhiteSpace(sense.Definition))
            return (string.Empty, null, null);

        var definition = sense.Definition;

        // 1. Extract domain/usage labels FIRST
        var (domain, usage) = ExtractDomainAndUsageLabels(definition);

        // 2. Remove etymology section
        definition = RemoveEtymologySection(definition);

        // 3. Remove Chinese translations (pattern: English text • Chinese text)
        definition = RemoveChineseTranslations(definition);

        // 4. Remove structured section markers
        definition = RemoveStructuredSections(definition);

        // 5. Remove POS markers and other formatting
        definition = CleanFormatting(definition);

        // 6. Remove any remaining domain/usage brackets from definition text
        if (!string.IsNullOrEmpty(domain) || !string.IsNullOrEmpty(usage))
        {
            definition = RemoveDomainLabelsFromText(definition, domain, usage);
        }

        // 7. Extract main English definition (text before any remaining markers)
        definition = ExtractMainEnglishText(definition);

        return (definition.Trim(), domain, usage);
    }

    private static (string? domain, string? usage) ExtractDomainAndUsageLabels(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);

        string? domain = null;
        string? usage = null;

        // Look for common domain labels in brackets
        var bracketMatches = Regex.Matches(text, @"\[([^\]]+)\]");
        foreach (Match match in bracketMatches)
        {
            var label = match.Groups[1].Value.Trim();
            var labelLower = label.ToLowerInvariant();

            // Usage labels
            if (labelLower.Contains("informal")) usage = "informal";
            else if (labelLower.Contains("formal")) usage = "formal";
            else if (labelLower.Contains("dated")) usage = "dated";
            else if (labelLower.Contains("archaic")) usage = "archaic";
            else if (labelLower.Contains("slang")) usage = "slang";
            else if (labelLower.Contains("humorous")) usage = "humorous";
            else if (labelLower.Contains("literary")) usage = "literary";
            else if (labelLower.Contains("technical")) usage = "technical";
            else if (labelLower.Contains("euphemistic")) usage = "euphemistic";
            else if (labelLower.Contains("derogatory")) usage = "derogatory";
            else if (labelLower.Contains("offensive")) usage = "offensive";

            // Domain labels
            else if (labelLower.Contains("music")) domain = "Music";
            else if (labelLower.Contains("law")) domain = "Law";
            else if (labelLower.Contains("medicine")) domain = "Medicine";
            else if (labelLower.Contains("biology")) domain = "Biology";
            else if (labelLower.Contains("chemistry")) domain = "Chemistry";
            else if (labelLower.Contains("physics")) domain = "Physics";
            else if (labelLower.Contains("mathematics")) domain = "Mathematics";
            else if (labelLower.Contains("computing")) domain = "Computing";
            else if (labelLower.Contains("finance")) domain = "Finance";
            else if (labelLower.Contains("business")) domain = "Business";
            else if (labelLower.Contains("military")) domain = "Military";
            else if (labelLower.Contains("nautical")) domain = "Nautical";
            else if (labelLower.Contains("aviation")) domain = "Aviation";
            else if (labelLower.Contains("sports")) domain = "Sports";
            else if (labelLower.Contains("art")) domain = "Art";
            else if (labelLower.Contains("philosophy")) domain = "Philosophy";
            else if (labelLower.Contains("theology")) domain = "Theology";

            // Regional labels
            else if (labelLower.Contains("n. amer.") || labelLower.Contains("north american"))
                domain = "North American";
            else if (labelLower.Contains("british")) domain = "British";
            else if (labelLower.Contains("australian")) domain = "Australian";
            else if (labelLower.Contains("canadian")) domain = "Canadian";
            else if (labelLower.Contains("new zealand")) domain = "New Zealand";

            // If not matched above but looks like a domain, use it as domain
            else if (label.Length > 0 && char.IsUpper(label[0]) && !label.Contains(" "))
                domain = label;
        }

        return (domain, usage);
    }

    private static string RemoveDomainLabelsFromText(string text, string? domain, string? usage)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var result = text;

        // Remove domain brackets
        if (!string.IsNullOrEmpty(domain))
        {
            result = Regex.Replace(result, $@"\s*\[{Regex.Escape(domain)}\]\s*", " ");
        }

        // Remove usage brackets
        if (!string.IsNullOrEmpty(usage))
        {
            result = Regex.Replace(result, $@"\s*\[{Regex.Escape(usage)}\]\s*", " ");
        }

        // Remove any remaining brackets
        result = Regex.Replace(result, @"\s*\[[^\]]*\]\s*", " ");

        return result.Trim();
    }

    private static string RemoveChineseTranslations(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove pattern: English text • Chinese text
        text = Regex.Replace(text, @"•\s*[\u4e00-\u9fff].*?(?=\n|$)", "", RegexOptions.Multiline);

        // Remove any remaining Chinese characters
        text = Regex.Replace(text, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        text = Regex.Replace(text, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        return text;
    }

    private static string RemoveStructuredSections(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove section markers but keep their content
        var sections = new[] { "【Examples】", "【SeeAlso】", "【Usage】", "【Grammar】",
                              "【Variants】", "【Pronunciation】", "【IDIOMS】", "【派生】",
                              "【Chinese】", "【Label】", "【语源】", "【用法】", "【PHR V】" };

        foreach (var section in sections)
        {
            text = text.Replace(section, "");
        }

        return text;
    }

    private static string ExtractMainEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Split by lines and take only lines that look like English definition
        var lines = text.Split('\n');
        var englishLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip lines that are clearly examples or markers
            if (trimmed.StartsWith("»") || trimmed.StartsWith("--›") ||
                trimmed.StartsWith("◘") || trimmed.Contains("IDIOMS"))
                continue;

            // Skip lines that are just numbers (sense numbers)
            if (Regex.IsMatch(trimmed, @"^\d+\.?\s*$"))
                continue;

            // Keep lines with reasonable English content
            if (trimmed.Length > 3 && ContainsEnglishWords(trimmed))
            {
                englishLines.Add(trimmed);
            }
        }

        return string.Join(" ", englishLines).Trim();
    }

    private static bool ContainsEnglishWords(string text)
    {
        // Check if text contains reasonable English word patterns
        return Regex.IsMatch(text, @"\b[a-zA-Z]{2,}\b") &&
               !Regex.IsMatch(text, @"^[\d\s\.]+$");
    }

    private static string ExtractCleanEtymology(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // Look for etymology section
        var etymologyMatch = Regex.Match(definition, @"【语源】\s*(.+?)(?:\n【|$)");
        if (!etymologyMatch.Success)
            return null;

        var etymology = etymologyMatch.Groups[1].Value.Trim();

        // Clean the etymology - remove sense numbers at beginning
        etymology = Regex.Replace(etymology, @"^\d+\.\s*", "");

        // Remove Chinese characters
        etymology = Regex.Replace(etymology, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        etymology = Regex.Replace(etymology, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        // Clean formatting
        etymology = CleanFormatting(etymology);

        return !string.IsNullOrWhiteSpace(etymology) ? etymology : null;
    }

    private static string RemoveEtymologySection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove the entire 【语源】 section
        return Regex.Replace(text, @"【语源】[^\n]*(\n[^\n【]*)*", "");
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

    private static string PreserveRawFragment(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        // Keep original but clean Chinese content
        var fragment = rawFragment;

        // Remove Chinese characters
        fragment = Regex.Replace(fragment, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        fragment = Regex.Replace(fragment, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        // Remove Chinese translation markers (keep the English word)
        fragment = Regex.Replace(fragment, @"\[([A-Za-z]+)\]", "$1");

        // Clean up
        fragment = Regex.Replace(fragment, @"\s+", " ").Trim();

        return fragment;
    }

    private static string BuildCleanStructuredDefinition(OxfordRawEntry entry, OxfordSenseRaw sense,
        string cleanDefinition, string? domain, string? usage)
    {
        var parts = new List<string>();

        // Add domain/usage information if available
        var labelParts = new List<string>();
        if (!string.IsNullOrEmpty(domain))
            labelParts.Add($"[{domain}]");
        if (!string.IsNullOrEmpty(usage))
            labelParts.Add($"[{usage}]");

        if (labelParts.Count > 0)
        {
            parts.Add($"【Label】{string.Join(", ", labelParts)}");
        }

        // Add the clean English definition
        if (!string.IsNullOrWhiteSpace(cleanDefinition))
        {
            parts.Add(cleanDefinition);
        }

        // Add Examples section if we have them
        if (sense.Examples != null && sense.Examples.Count > 0)
        {
            var englishExamples = new List<string>();
            foreach (var example in sense.Examples)
            {
                var cleanExample = CleanFormatting(example);
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
}