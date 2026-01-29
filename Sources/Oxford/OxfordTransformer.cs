using System.Globalization;
using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;
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
            var normalizedWord = Helper.NormalizeWordWithSourceContext(raw.Headword, SourceCode);

            // Process each sense with proper sequencing
            int globalSenseNumber = 1;

            foreach (var sense in raw.Senses.OrderBy(s => s.SenseNumber))
            {
                // 1. Extract core components
                var (mainDefinition, cleanEtymology, domain, usage) = ExtractCoreComponents(sense);

                // 2. Determine POS with confidence
                var (pos, posConfidence) = DeterminePartOfSpeech(raw, sense, mainDefinition);

                // 3. Clean RawFragment (preserve original but clean)
                var cleanRawFragment = CleanRawFragment(sense.Definition);

                // 4. Build structured definition
                var structuredDefinition = BuildStructuredDefinition(
                    mainDefinition, cleanEtymology, domain, usage, sense.Examples);

                // 5. Create entry with proper sense number
                var entry = new DictionaryEntry
                {
                    Word = raw.Headword,
                    NormalizedWord = normalizedWord,
                    PartOfSpeech = pos,
                    PartOfSpeechConfidence = Helper.NormalizePartOfSpeechConfidence(posConfidence),
                    Definition = structuredDefinition,
                    Etymology = cleanEtymology,
                    SenseNumber = sense.SenseNumber > 0 ? sense.SenseNumber : globalSenseNumber,
                    SourceCode = SourceCode,
                    CreatedUtc = DateTime.UtcNow,
                    RawFragment = cleanRawFragment
                };

                entries.Add(entry);
                globalSenseNumber++;
            }

            Helper.LogProgress(logger, SourceCode, Helper.GetCurrentCount(SourceCode));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to transform Oxford entry: {Headword}", raw?.Headword);
        }

        return entries;
    }

    private static (string mainDefinition, string? etymology, string? domain, string? usage)
        ExtractCoreComponents(OxfordSenseRaw sense)
    {
        if (string.IsNullOrWhiteSpace(sense.Definition))
            return (string.Empty, null, null, null);

        var definition = sense.Definition;

        // 1. Extract etymology first
        var etymology = ExtractCleanEtymology(definition);
        if (!string.IsNullOrEmpty(etymology))
        {
            definition = Regex.Replace(definition, @"【语源】[^\n]*(\n[^\n【]*)*", "");
        }

        // 2. Extract domain and usage labels
        var (domain, usage) = ExtractDomainAndUsage(definition);

        // 3. Remove all structured sections
        definition = RemoveAllStructuredSections(definition);

        // 4. Extract main English definition
        var mainDefinition = ExtractMainEnglishDefinition(definition);

        // 5. Final cleanup
        mainDefinition = CleanFinalDefinition(mainDefinition, domain, usage);

        return (mainDefinition, etymology, domain, usage);
    }

    private static string? ExtractCleanEtymology(string text)
    {
        var match = Regex.Match(text, @"【语源】\s*(.+?)(?:\n【|$)");
        if (!match.Success)
            return null;

        var etymology = match.Groups[1].Value.Trim();

        // Remove sense numbers at beginning
        etymology = Regex.Replace(etymology, @"^\d+\.\s*", "");

        // Remove all non-English characters
        etymology = Regex.Replace(etymology, @"[^\x00-\x7F]", " ");

        // Clean formatting
        etymology = Regex.Replace(etymology, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(etymology) ? null : etymology;
    }

    private static (string? domain, string? usage) ExtractDomainAndUsage(string text)
    {
        var domain = new List<string>();
        var usage = new List<string>();

        // Extract from brackets
        var bracketMatches = Regex.Matches(text, @"\[([^\]]+)\]");
        foreach (Match match in bracketMatches)
        {
            var label = match.Groups[1].Value.Trim();
            var lowerLabel = label.ToLowerInvariant();

            // Usage labels
            if (lowerLabel == "informal" || lowerLabel == "formal" ||
                lowerLabel == "dated" || lowerLabel == "archaic" ||
                lowerLabel == "slang" || lowerLabel == "humorous" ||
                lowerLabel == "literary" || lowerLabel == "technical" ||
                lowerLabel == "euphemistic" || lowerLabel == "derogatory" ||
                lowerLabel == "offensive" || lowerLabel == "vulgar")
            {
                usage.Add(label);
            }
            // Domain labels
            else if (lowerLabel == "music" || lowerLabel == "law" ||
                     lowerLabel == "medicine" || lowerLabel == "biology" ||
                     lowerLabel == "chemistry" || lowerLabel == "physics" ||
                     lowerLabel == "mathematics" || lowerLabel == "computing" ||
                     lowerLabel == "finance" || lowerLabel == "business" ||
                     lowerLabel == "military" || lowerLabel == "nautical" ||
                     lowerLabel == "aviation" || lowerLabel == "sports" ||
                     lowerLabel == "art" || lowerLabel == "philosophy" ||
                     lowerLabel == "theology" || lowerLabel == "anatomy" ||
                     lowerLabel == "botany" || lowerLabel == "zoology")
            {
                domain.Add(CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lowerLabel));
            }
            // Regional labels
            else if (lowerLabel.Contains("n. amer.") || lowerLabel.Contains("north american"))
                domain.Add("North American");
            else if (lowerLabel.Contains("brit") || lowerLabel == "brit")
                domain.Add("British");
            else if (lowerLabel.Contains("australian"))
                domain.Add("Australian");
            else if (lowerLabel.Contains("canadian"))
                domain.Add("Canadian");
            else if (lowerLabel.Contains("new zealand"))
                domain.Add("New Zealand");
            // Assume it's a domain if it starts with capital and is short
            else if (label.Length <= 20 && char.IsUpper(label[0]) && !label.Contains(" "))
                domain.Add(label);
        }

        return (
            domain.Count > 0 ? string.Join("; ", domain.Distinct()) : null,
            usage.Count > 0 ? string.Join("; ", usage.Distinct()) : null
        );
    }

    private static string RemoveAllStructuredSections(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove all section markers and their content
        var sections = new[] {
            "【Examples】", "【SeeAlso】", "【Usage】", "【Grammar】",
            "【Variants】", "【Pronunciation】", "【IDIOMS】", "【派生】",
            "【Chinese】", "【Label】", "【语源】", "【用法】", "【PHR V】"
        };

        foreach (var section in sections)
        {
            var pattern = $@"{Regex.Escape(section)}[^\n]*(\n[^\n【]*)*";
            text = Regex.Replace(text, pattern, "");
        }

        return text;
    }

    private static string ExtractMainEnglishDefinition(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Split by lines and filter
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var definitionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip obviously non-definition lines
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("»") ||
                trimmed.StartsWith("--›") ||
                trimmed.StartsWith("◘") ||
                Regex.IsMatch(trimmed, @"^【") ||
                Regex.IsMatch(trimmed, @"^\d+\.?\s*$")) // Just a number
                continue;

            // Remove Chinese characters and markers
            var cleanLine = Regex.Replace(trimmed, @"[\u4e00-\u9fff]", "");
            cleanLine = Regex.Replace(cleanLine, @"•.*", "").Trim();

            // Remove sense numbers at beginning
            cleanLine = Regex.Replace(cleanLine, @"^\d+\.\s*", "");

            // Check if line contains reasonable English content
            if (cleanLine.Length >= 3 && ContainsReasonableEnglish(cleanLine))
            {
                definitionLines.Add(cleanLine);
            }
        }

        var definition = string.Join(" ", definitionLines).Trim();

        // If no definition found, try to extract from first meaningful line
        if (string.IsNullOrWhiteSpace(definition))
        {
            foreach (var line in lines)
            {
                var cleanLine = Regex.Replace(line, @"[\u4e00-\u9fff•]", "").Trim();
                cleanLine = Regex.Replace(cleanLine, @"^\d+\.\s*", "");

                if (cleanLine.Length >= 5 && ContainsReasonableEnglish(cleanLine))
                {
                    return cleanLine;
                }
            }
        }

        return definition;
    }

    private static bool ContainsReasonableEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Must contain at least one English word of reasonable length
        if (!Regex.IsMatch(text, @"\b[a-zA-Z]{3,}\b"))
            return false;

        // Reject lines that are just formatting or symbols
        if (Regex.IsMatch(text, @"^[•\-\[\]\s]+$"))
            return false;

        // Check word count
        var wordCount = Regex.Matches(text, @"\b[a-zA-Z]{2,}\b").Count;
        return wordCount >= 1;
    }

    private static string CleanFinalDefinition(string definition, string? domain, string? usage)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var clean = definition;

        // Remove domain/usage brackets if they appear in text
        if (!string.IsNullOrEmpty(domain))
        {
            foreach (var d in domain.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedDomain = d.Trim();
                clean = Regex.Replace(clean, $@"\s*\[{Regex.Escape(trimmedDomain)}\]\s*", " ", RegexOptions.IgnoreCase);
            }
        }

        if (!string.IsNullOrEmpty(usage))
        {
            foreach (var u in usage.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmedUsage = u.Trim();
                clean = Regex.Replace(clean, $@"\s*\[{Regex.Escape(trimmedUsage)}\]\s*", " ", RegexOptions.IgnoreCase);
            }
        }

        // Remove any remaining brackets
        clean = Regex.Replace(clean, @"\s*\[[^\]]*\]\s*", " ");

        // Remove formatting artifacts
        clean = Regex.Replace(clean, @"[•▶»◘›♦\-]", " ");

        // Clean whitespace
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        // Ensure proper sentence ending
        if (!string.IsNullOrWhiteSpace(clean) &&
            !clean.EndsWith(".") && !clean.EndsWith("!") && !clean.EndsWith("?") &&
            clean.Length > 10)
        {
            clean += ".";
        }

        return clean;
    }

    private static (string pos, int confidence) DeterminePartOfSpeech(OxfordRawEntry raw, OxfordSenseRaw sense, string definition)
    {
        int confidence = 0;
        string pos = "unk";

        // Priority 1: Explicit ▶ marker (highest confidence)
        if (!string.IsNullOrWhiteSpace(sense.SenseLabel))
        {
            var posMatch = Regex.Match(sense.SenseLabel,
                @"▶\s*(noun|verb|adjective|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|abbreviation|symbol|combining form|phrasal verb|idiom|particle)",
                RegexOptions.IgnoreCase);

            if (posMatch.Success)
            {
                pos = OxfordSourceDataHelper.NormalizePartOfSpeech(posMatch.Groups[1].Value);
                confidence = 95;
                return (pos, confidence);
            }
        }

        // Priority 2: Check raw.PartOfSpeech
        if (!string.IsNullOrWhiteSpace(raw.PartOfSpeech))
        {
            pos = OxfordSourceDataHelper.NormalizePartOfSpeech(raw.PartOfSpeech);
            confidence = 85;
            return (pos, confidence);
        }

        // Priority 3: Check definition for POS clues
        if (!string.IsNullOrWhiteSpace(definition))
        {
            // Check for "mass noun", "plural noun", etc.
            if (Regex.IsMatch(definition, @"\b(mass noun|plural noun|count noun|uncountable noun)\b", RegexOptions.IgnoreCase))
            {
                pos = "noun";
                confidence = 80;
                return (pos, confidence);
            }

            if (Regex.IsMatch(definition, @"\b(transitive verb|intransitive verb|phrasal verb)\b", RegexOptions.IgnoreCase))
            {
                pos = "verb";
                confidence = 80;
                return (pos, confidence);
            }
        }

        // Priority 4: Infer from headword patterns
        if (!string.IsNullOrWhiteSpace(raw.Headword))
        {
            pos = InferPosFromHeadwordPatterns(raw.Headword);
            if (pos != "unk")
            {
                confidence = 70;
                return (pos, confidence);
            }
        }

        // Default
        return ("unk", 0);
    }

    private static string InferPosFromHeadwordPatterns(string headword)
    {
        var hw = headword.ToLowerInvariant();

        // Common suffixes
        var suffixPatterns = new Dictionary<string, string>
        {
            { @"(ness|ment|tion|sion|ity|ance|ence|hood|ship|ism|ist)$", "noun" },
            { @"(able|ible|ive|ous|ful|less|ish|ic|al|ical|ary|ory|ant|ent)$", "adj" },
            { @"ly$", "adv" },
            { @"(ize|ise|ify|ate|en)$", "verb" },
            { @"(logy|graphy|metry|nomy|phobia|mania|scope|phone)$", "noun" }
        };

        foreach (var pattern in suffixPatterns)
        {
            if (Regex.IsMatch(hw, pattern.Key))
                return pattern.Value;
        }

        // Check for prefix patterns
        if (Regex.IsMatch(hw, @"^(un|re|dis|mis|pre|post|over|under|sub|inter|intra|trans)"))
            return "prefix";

        // Check for combining forms
        if (hw.Contains("-") && hw.Split('-').All(p => p.Length >= 2))
            return "combining form";

        return "unk";
    }

    private static string CleanRawFragment(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var fragment = rawFragment;

        // Remove Chinese characters
        fragment = Regex.Replace(fragment, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        fragment = Regex.Replace(fragment, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        // Remove section markers
        fragment = Regex.Replace(fragment, @"【[^】]*】", "");

        // Remove formatting artifacts but preserve content
        fragment = Regex.Replace(fragment, @"[▶»◘›♦]", " ");
        fragment = Regex.Replace(fragment, @"•\s*", " ");

        // Clean up
        fragment = Regex.Replace(fragment, @"\s+", " ").Trim();

        // Limit length
        if (fragment.Length > 500)
            fragment = fragment.Substring(0, 500).Trim() + "...";

        return fragment;
    }

    private static string BuildStructuredDefinition(
        string mainDefinition,
        string? etymology,
        string? domain,
        string? usage,
        List<string>? examples)
    {
        var parts = new List<string>();

        // Add label section if we have domain or usage
        var labelParts = new List<string>();
        if (!string.IsNullOrEmpty(domain))
            labelParts.Add($"[{domain}]");
        if (!string.IsNullOrEmpty(usage))
            labelParts.Add($"[{usage}]");

        if (labelParts.Count > 0)
            parts.Add($"【Label】{string.Join(", ", labelParts)}");

        // Add main definition
        if (!string.IsNullOrWhiteSpace(mainDefinition))
            parts.Add(mainDefinition);

        // Add examples if available
        if (examples != null && examples.Count > 0)
        {
            var cleanExamples = new List<string>();
            foreach (var example in examples)
            {
                var cleanExample = CleanExample(example);
                if (!string.IsNullOrWhiteSpace(cleanExample))
                    cleanExamples.Add(cleanExample);
            }

            if (cleanExamples.Count > 0)
            {
                parts.Add("【Examples】");
                foreach (var example in cleanExamples)
                    parts.Add($"» {example}");
            }
        }

        // Add etymology if available (but not in main definition area)
        if (!string.IsNullOrWhiteSpace(etymology))
        {
            parts.Add("【Etymology】");
            parts.Add(etymology);
        }

        return string.Join("\n", parts);
    }

    private static string CleanExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        var clean = example;

        // Remove » marker if present
        clean = clean.TrimStart('»', ' ').Trim();

        // Remove Chinese characters
        clean = Regex.Replace(clean, @"[\u4e00-\u9fff]", "");

        // Remove brackets with content
        clean = Regex.Replace(clean, @"\[[^\]]*\]", "");

        // Clean formatting
        clean = Regex.Replace(clean, @"[•◘♦]", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        return clean;
    }
}