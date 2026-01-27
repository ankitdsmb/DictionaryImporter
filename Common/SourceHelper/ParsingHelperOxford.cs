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

        foreach (Match match in Regex.Matches(definition, @"\(([^)]+)\)"))
        {
            var candidate = CleanDomainText(match.Groups[1].Value);

            // Reject morphology
            if (Regex.IsMatch(candidate, @"easier|easiest|plural|past|comparative|superlative",
                RegexOptions.IgnoreCase))
                continue;

            if (IsValidOxfordDomain(candidate))
                return candidate.Length <= 100 ? candidate : candidate[..100];
        }

        var label = Helper.ExtractSection(definition, "【Label】");
        if (!string.IsNullOrWhiteSpace(label))
        {
            label = CleanDomainText(label);
            if (IsValidOxfordDomain(label))
                return label.Length <= 100 ? label : label[..100];
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

        var match = Regex.Match(definition, @"/([^/]+)/");
        if (!match.Success)
            return null;

        var ipa = match.Groups[1].Value.Trim();
        return ContainsIpaCharacters(ipa) ? ipa : null;
    }

    // ─────────────────────────────────────────────
    // VARIANTS
    // ─────────────────────────────────────────────
    public static IReadOnlyList<string> ExtractVariants(string definition)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(definition))
            return variants.ToList();

        var section = Helper.ExtractSection(definition, "【Variants】");
        if (!string.IsNullOrWhiteSpace(section))
        {
            foreach (var v in section.Split(',', ';'))
            {
                var s = v.Trim();
                if (!string.IsNullOrEmpty(s))
                    variants.Add(s);
            }
        }

        var also = Regex.Match(definition, @"也作\s*([^),]+)");
        if (also.Success)
        {
            foreach (var v in also.Groups[1].Value.Split(',', ';'))
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

        // Remove stars only
        text = Regex.Replace(text, @"^★+☆+\s*", "");

        // Remove sense numbers
        text = Regex.Replace(text, @"^\d+\.\s*", "");

        // Remove domain parentheses only
        if (!string.IsNullOrWhiteSpace(extractedDomain))
            text = Regex.Replace(text, @"\([^)]*" + Regex.Escape(extractedDomain) + @"[^)]*\)", "");

        // Remove IPA
        text = Regex.Replace(text, @"/[^/]+/", "");

        // Remove structured sections
        foreach (var sec in new[] { "【Examples】", "【SeeAlso】", "【Usage】", "【Grammar】", "【Variants】", "【Pronunciation】", "【Etymology】", "【IDIOMS】", "【派生】" })
            text = RemoveSection(text, sec);

        // English part only
        text = text.Split('•', 2)[0];

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
        domain = Regex.Replace(domain, @"[\u4e00-\u9fff]", "");
        domain = Regex.Replace(domain, @"\s+", " ").Trim();
        return domain.Trim('.', ',', ';', ':');
    }

    private static bool ContainsIpaCharacters(string text)
        => Regex.IsMatch(text, @"[ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒ]");

    private static bool IsValidOxfordDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return false;

        var keywords = new[]
        {
            "informal","formal","technical","literary","humorous","dated","archaic",
            "slang","colloquial","dialect","regional","chiefly","mainly","especially"
        };

        return keywords.Any(k => domain.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    // ─────────────────────────────────────────────
    // SYNONYMS (UNCHANGED, SAFE)
    // ─────────────────────────────────────────────
    public static IReadOnlyList<string>? ExtractSynonymsFromExamples(IReadOnlyList<string> examples)
    {
        if (examples == null || examples.Count == 0)
            return null;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var example in examples)
        {
            foreach (Match m in Regex.Matches(example,
                         @"\b([A-Z][a-z]+)\b\s*(?:or|synonym|same as)\s*\b([A-Z][a-z]+)\b"))
            {
                set.Add(m.Groups[1].Value);
                set.Add(m.Groups[2].Value);
            }
        }

        return set.Count > 0 ? set.ToList() : null;
    }
}