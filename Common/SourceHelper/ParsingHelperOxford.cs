using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Common.SourceHelper;

internal static class ParsingHelperOxford
{
    // ─────────────────────────────────────────────
    // EXAMPLES - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static IReadOnlyList<string> ExtractExamples(string definition)
    {
        var examples = new List<string>();
        if (string.IsNullOrWhiteSpace(definition))
            return examples;

        var lines = definition.Split('\n');
        var inExamples = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("【Examples】"))
            {
                inExamples = true;
                // Check if there's example text on the same line
                var rest = line.Substring("【Examples】".Length).Trim();
                if (!string.IsNullOrWhiteSpace(rest))
                {
                    // Handle example on same line as marker
                    ProcessExampleLine(rest, examples);
                }
                continue;
            }

            if (!inExamples)
                continue;

            // Stop at next section marker or empty line
            if (line.StartsWith("【") || string.IsNullOrWhiteSpace(line))
                break;

            ProcessExampleLine(line, examples);
        }

        return examples;
    }

    private static void ProcessExampleLine(string line, List<string> examples)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        // Handle example marker or plain text
        if (line.StartsWith("»"))
        {
            var example = line.TrimStart('»', ' ').Trim();
            if (!string.IsNullOrWhiteSpace(example))
                examples.Add(example);
        }
        else if (!line.StartsWith("【")) // Not a section marker
        {
            // Could be example without marker in current broken data
            examples.Add(line);
        }
    }

    // ─────────────────────────────────────────────
    // CROSS REFERENCES - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
    {
        var crossRefs = new List<CrossReference>();

        if (string.IsNullOrWhiteSpace(definition))
            return crossRefs;

        // Check SeeAlso section - handle broken markers
        var seeAlso = ExtractSectionWithBrokenMarkers(definition, "【SeeAlso】");
        if (!string.IsNullOrWhiteSpace(seeAlso))
        {
            foreach (var part in seeAlso.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = part.Trim();
                if (!string.IsNullOrEmpty(word))
                {
                    crossRefs.Add(new CrossReference
                    {
                        TargetWord = word,
                        ReferenceType = "SeeAlso"
                    });
                }
            }
        }

        // Extract cross-references from definition text
        // Pattern 1: --› see word
        var seeMatches = Regex.Matches(definition, @"--›\s*(?:see|cf\.?|compare)\s+([A-Za-z\-']+)");
        foreach (Match match in seeMatches)
        {
            if (match.Groups[1].Success)
            {
                crossRefs.Add(new CrossReference
                {
                    TargetWord = match.Groups[1].Value.Trim(),
                    ReferenceType = "SeeAlso"
                });
            }
        }

        // Pattern 2: variant of word
        var variantMatches = Regex.Matches(definition, @"(?:variant of|another term for|同)\s+([A-Za-z\-']+)");
        foreach (Match match in variantMatches)
        {
            if (match.Groups[1].Success)
            {
                crossRefs.Add(new CrossReference
                {
                    TargetWord = match.Groups[1].Value.Trim(),
                    ReferenceType = "Variant"
                });
            }
        }

        // Pattern 3: (also word)
        var alsoMatches = Regex.Matches(definition, @"\(also\s+([A-Za-z\-']+)\)");
        foreach (Match match in alsoMatches)
        {
            if (match.Groups[1].Success)
            {
                crossRefs.Add(new CrossReference
                {
                    TargetWord = match.Groups[1].Value.Trim(),
                    ReferenceType = "Also"
                });
            }
        }

        return crossRefs;
    }

    private static string? ExtractSectionWithBrokenMarkers(string definition, string marker)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        var idx = definition.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return null;

        var start = idx + marker.Length;
        var remaining = definition.Substring(start);

        // Find next section marker or end
        var nextSection = remaining.IndexOf("【", StringComparison.Ordinal);
        if (nextSection >= 0)
        {
            return remaining.Substring(0, nextSection).Trim();
        }

        return remaining.Trim();
    }

    // ─────────────────────────────────────────────
    // MAIN ENTRY PARSER - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static OxfordParsedData ParseOxfordEntry(string definition)
    {
        var data = new OxfordParsedData();
        if (string.IsNullOrWhiteSpace(definition))
            return data;

        data.Domain = ExtractOxfordDomain(definition);
        data.IpaPronunciation = ExtractIpaPronunciation(definition);
        data.Variants = ExtractVariants(definition);
        data.UsageLabel = ExtractUsageLabel(definition);
        data.CleanDefinition = ExtractMainDefinition(definition);

        // POS is resolved earlier in pipeline — DO NOT override here
        data.PartOfSpeech = null;

        return data;
    }

    // ─────────────────────────────────────────────
    // DOMAIN / REGISTER - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static string? ExtractOxfordDomain(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // First check for explicit Label section
        var label = ExtractSectionWithBrokenMarkers(definition, "【Label】");
        if (!string.IsNullOrWhiteSpace(label))
        {
            label = CleanDomainText(label);
            if (IsValidOxfordDomain(label))
                return label.Length <= 100 ? label : label[..100];
        }

        // Look for square bracket labels in definition: [informal], [dated], [N. Amer.], etc.
        var bracketMatches = Regex.Matches(definition, @"\[([^\]]+)\]");
        foreach (Match match in bracketMatches)
        {
            var candidate = CleanDomainText(match.Groups[1].Value);
            if (IsValidOxfordDomain(candidate))
                return candidate.Length <= 100 ? candidate : candidate[..100];
        }

        // Then check parentheses throughout definition
        foreach (Match match in Regex.Matches(definition, @"\(([^)]+)\)"))
        {
            var candidate = CleanDomainText(match.Groups[1].Value);

            // Skip morphological markers
            if (Regex.IsMatch(candidate,
                @"^(easier|easiest|plural|past|comparative|superlative|past tense|past participle|present participle|third person singular|ing form|ed form)$",
                RegexOptions.IgnoreCase))
                continue;

            // Skip size/measurement references
            if (Regex.IsMatch(candidate, @"^\d+\s*(cm|mm|m|kg|g|ml|l)$", RegexOptions.IgnoreCase))
                continue;

            // Skip numeric-only or symbol-only
            if (Regex.IsMatch(candidate, @"^[\d\s\-]+$") || candidate.Length < 2)
                continue;

            if (IsValidOxfordDomain(candidate))
                return candidate.Length <= 100 ? candidate : candidate[..100];
        }

        return null;
    }

    // ─────────────────────────────────────────────
    // IPA - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static string? ExtractIpaPronunciation(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // First check Pronunciation section
        var pronSection = ExtractSectionWithBrokenMarkers(definition, "【Pronunciation】");
        if (!string.IsNullOrWhiteSpace(pronSection))
        {
            var slashMatch = Regex.Match(pronSection, @"/([^/]+)/");
            if (slashMatch.Success)
            {
                var ipa = slashMatch.Groups[1].Value.Trim();
                if (ContainsIpaCharacters(ipa))
                    return ipa;
            }

            // Also check if the section itself contains IPA
            if (ContainsIpaCharacters(pronSection))
                return pronSection.Trim();
        }

        // Fallback: search for IPA in slashes anywhere in definition
        var matches = Regex.Matches(definition, @"/([^/\n]+)/");
        foreach (Match match in matches)
        {
            var ipa = match.Groups[1].Value.Trim();
            if (ContainsIpaCharacters(ipa))
                return ipa;
        }

        return null;
    }

    // ─────────────────────────────────────────────
    // VARIANTS - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static IReadOnlyList<string> ExtractVariants(string definition)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(definition))
            return variants.ToList();

        // Check Variants section
        var section = ExtractSectionWithBrokenMarkers(definition, "【Variants】");
        if (!string.IsNullOrWhiteSpace(section))
        {
            foreach (var v in section.Split(new[] { ',', ';', '，', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = v.Trim();
                if (!string.IsNullOrEmpty(s))
                    variants.Add(s);
            }
        }

        return variants.ToList();
    }

    // ─────────────────────────────────────────────
    // USAGE / GRAMMAR - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static string? ExtractUsageLabel(string definition)
    {
        var usage = ExtractSectionWithBrokenMarkers(definition, "【Usage】");
        if (!string.IsNullOrWhiteSpace(usage))
        {
            return usage.Length <= 50 ? usage : usage[..50];
        }

        var grammar = ExtractSectionWithBrokenMarkers(definition, "【Grammar】");
        if (!string.IsNullOrWhiteSpace(grammar))
        {
            return grammar.Length <= 50 ? grammar : grammar[..50];
        }

        return null;
    }

    // ─────────────────────────────────────────────
    // CLEAN DEFINITION (SAFE) - FIXED to handle current broken data AND fix regex error
    // ─────────────────────────────────────────────
    public static string ExtractMainDefinition(string definition)
        => CleanOxfordDefinition(definition);

    public static string CleanOxfordDefinition(string definition, string? extractedDomain = null)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var text = definition;

        // Remove stars only at the beginning of the string
        text = Regex.Replace(text, @"^★+☆+\s*", "");

        // Remove sense numbers at beginning of lines
        text = Regex.Replace(text, @"^\d+\.\s*", "", RegexOptions.Multiline);

        // Remove IPA pronunciation - all variants
        text = Regex.Replace(text, @"/[^/\n]+/", "");

        // Remove pronunciation section entirely
        text = RemoveSectionWithBrokenMarkers(text, "【Pronunciation】");

        // Remove all structured sections (but keep the content after them)
        var sections = new[] {
        "【Examples】", "【SeeAlso】", "【Usage】", "【Grammar】",
        "【Variants】", "【Pronunciation】", "【Etymology】",
        "【IDIOMS】", "【派生】", "【Chinese】", "【Label】",
        "【语源】", "【用法】", "【PHR V】"
    };

        foreach (var sec in sections)
            text = RemoveSectionWithBrokenMarkers(text, sec);

        // Remove formatting artifacts but keep meaningful content
        // FIXED: Changed [▶»◘--›♦] to [▶»◘›♦\-] to avoid reverse range error
        text = Regex.Replace(text, @"[▶»◘›♦\-]", " ");

        // Remove the string "?-->" (literal characters, not a range)
        text = Regex.Replace(text, @"\?--\>", " ");

        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Remove any remaining broken Chinese markers
        text = Regex.Replace(text, @"•\s*\[.*$", "").Trim();
        text = Regex.Replace(text, @"\[.*$", "").Trim();

        text = text.TrimEnd('.', ';', ':', ',');

        return text;
    }

    private static string RemoveSectionWithBrokenMarkers(string text, string marker)
    {
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return text;

        var after = text[(idx + marker.Length)..];
        var next = after.IndexOf("【", StringComparison.Ordinal);
        return next >= 0 ? text[..idx] + after[next..] : text[..idx];
    }

    private static string CleanDomainText(string domain)
    {
        domain = Regex.Replace(domain, @"[\u4e00-\u9fff]", ""); // Remove Chinese characters
        domain = Regex.Replace(domain, @"\s+", " ").Trim();
        return domain.Trim('.', ',', ';', ':', '(', ')', '[', ']');
    }

    private static bool ContainsIpaCharacters(string text)
        => Regex.IsMatch(text, @"[ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒʁβɣɸɮɱɳɲŋʎʟʋɹɻɰʔʕʢʡɓɗʄɠʛɦɬɮɭʎʟɺɾɽʀʁʕʢʡʘǀǃǂǁ]");

    private static bool IsValidOxfordDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        // Clean the domain text first
        domain = CleanDomainText(domain);
        domain = domain.ToLowerInvariant();

        // Oxford-specific domain labels
        var keywords = new[]
        {
        "informal", "formal", "technical", "literary", "humorous", "dated", "archaic",
        "slang", "colloquial", "dialect", "regional", "chiefly", "mainly", "especially",
        "rare", "obsolete", "vulgar", "offensive", "derogatory", "euphemistic",
        "figurative", "ironic", "sarcastic", "law", "medicine", "biology", "chemistry",
        "physics", "mathematics", "computing", "finance", "business", "military",
        "nautical", "aviation", "sports", "music", "art", "philosophy", "theology",
        "n. amer.", "north american", "british", "australian", "canadian", "new zealand"
    };

        // Check if domain starts with or contains a known keyword
        foreach (var keyword in keywords)
        {
            if (domain == keyword ||
                domain.StartsWith(keyword + " ") ||
                domain.Contains(" " + keyword + " ") ||
                domain.EndsWith(" " + keyword))
                return true;
        }

        return false;
    }

    // ─────────────────────────────────────────────
    // SYNONYMS (IMPROVED HANDLING) - FIXED to handle current broken data
    // ─────────────────────────────────────────────
    public static IReadOnlyList<string>? ExtractSynonymsFromExamples(IReadOnlyList<string> examples)
    {
        if (examples == null || examples.Count == 0)
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var example in examples)
        {
            if (string.IsNullOrWhiteSpace(example))
                continue;

            // Pattern for "X or Y" or "X (synonym of Y)" or "X, same as Y"
            foreach (Match m in Regex.Matches(example,
                         @"\b([A-Z][a-z]+(?:-[A-Za-z]+)*)\b\s*(?:or|synonym of|same as|also called)\s*\b([A-Z][a-z]+(?:-[A-Za-z]+)*)\b",
                         RegexOptions.IgnoreCase))
            {
                set.Add(m.Groups[1].Value);
                set.Add(m.Groups[2].Value);
            }

            // Also check for parenthetical synonyms
            foreach (Match m in Regex.Matches(example,
                         @"\(synonym\s*:\s*([^)]+)\)",
                         RegexOptions.IgnoreCase))
            {
                var synonyms = m.Groups[1].Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var syn in synonyms)
                {
                    var cleanSyn = syn.Trim();
                    if (!string.IsNullOrEmpty(cleanSyn))
                        set.Add(cleanSyn);
                }
            }
        }

        return set.Count > 0 ? set.ToList() : null;
    }
}