using System.Text.RegularExpressions;
using DictionaryImporter.Common;

namespace DictionaryImporter.Common.SourceHelper;

internal static class ParsingHelperOxford
{
    // ─────────────────────────────────────────────
    // EXAMPLES
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
                continue;
            }

            if (!inExamples)
                continue;

            if (line.StartsWith("【"))
                break;

            if (line.StartsWith("»"))
                examples.Add(line.TrimStart('»', ' ').Trim());
        }

        return examples;
    }

    // ─────────────────────────────────────────────
    // CROSS REFERENCES
    // ─────────────────────────────────────────────
    public static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
    {
        var crossRefs = new List<CrossReference>();
        var seeAlso = Helper.ExtractSection(definition, "【SeeAlso】");

        if (string.IsNullOrWhiteSpace(seeAlso))
            return crossRefs;

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

        return crossRefs;
    }

    // ─────────────────────────────────────────────
    // MAIN ENTRY PARSER
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
    // DOMAIN / REGISTER
    // ─────────────────────────────────────────────
    public static string? ExtractOxfordDomain(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // First check for explicit Label section
        var label = Helper.ExtractSection(definition, "【Label】");
        if (!string.IsNullOrWhiteSpace(label))
        {
            label = CleanDomainText(label);
            if (IsValidOxfordDomain(label))
                return label.Length <= 100 ? label : label[..100];
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
    // IPA
    // ─────────────────────────────────────────────
    public static string? ExtractIpaPronunciation(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return null;

        // First check Pronunciation section
        var pronSection = Helper.ExtractSection(definition, "【Pronunciation】");
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
    // VARIANTS
    // ─────────────────────────────────────────────
    public static IReadOnlyList<string> ExtractVariants(string definition)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(definition))
            return variants.ToList();

        // Check Variants section
        var section = Helper.ExtractSection(definition, "【Variants】");
        if (!string.IsNullOrWhiteSpace(section))
        {
            foreach (var v in section.Split(new[] { ',', ';', '，', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = v.Trim();
                if (!string.IsNullOrEmpty(s))
                    variants.Add(s);
            }
        }

        // Check Chinese "also" notation
        var also = Regex.Match(definition, @"也作\s*([^),;]+)");
        if (also.Success)
        {
            foreach (var v in also.Groups[1].Value.Split(new[] { ',', ';', '，', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = v.Trim();
                if (!string.IsNullOrEmpty(s))
                    variants.Add(s);
            }
        }

        return variants.ToList();
    }

    // ─────────────────────────────────────────────
    // USAGE / GRAMMAR
    // ─────────────────────────────────────────────
    public static string? ExtractUsageLabel(string definition)
    {
        var usage = Helper.ExtractSection(definition, "【Usage】");
        if (!string.IsNullOrWhiteSpace(usage))
            return usage.Length <= 50 ? usage : usage[..50];

        var grammar = Helper.ExtractSection(definition, "【Grammar】");
        if (!string.IsNullOrWhiteSpace(grammar))
            return grammar.Length <= 50 ? grammar : grammar[..50];

        return null;
    }

    // ─────────────────────────────────────────────
    // CLEAN DEFINITION (SAFE)
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
        text = RemoveSection(text, "【Pronunciation】");

        // Remove domain parentheses only if we extracted a domain from them
        if (!string.IsNullOrWhiteSpace(extractedDomain))
        {
            // Match parentheses containing the domain (case-insensitive)
            text = Regex.Replace(text,
                @"\([^)]*" + Regex.Escape(extractedDomain) + @"[^)]*\)",
                "",
                RegexOptions.IgnoreCase);
        }

        // Remove all structured sections
        var sections = new[] {
            "【Examples】", "【SeeAlso】", "【Usage】", "【Grammar】",
            "【Variants】", "【Pronunciation】", "【Etymology】",
            "【IDIOMS】", "【派生】", "【Chinese】", "【Label】"
        };

        foreach (var sec in sections)
            text = RemoveSection(text, sec);

        // Extract English part only (before Chinese bullet)
        var bulletSplit = text.Split('•', 2);
        if (bulletSplit.Length > 1)
            text = bulletSplit[0];

        // Clean up remaining markers
        text = Regex.Replace(text, @"[▶»]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = text.TrimEnd('.', ';', ':', ',');

        return text;
    }

    // ─────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────
    private static string RemoveSection(string text, string marker)
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
            "nautical", "aviation", "sports", "music", "art", "philosophy", "theology"
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
    // SYNONYMS (IMPROVED HANDLING)
    // ─────────────────────────────────────────────
    public static IReadOnlyList<string>? ExtractSynonymsFromExamples(IReadOnlyList<string> examples)
    {
        if (examples == null || examples.Count == 0)
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var example in examples)
        {
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