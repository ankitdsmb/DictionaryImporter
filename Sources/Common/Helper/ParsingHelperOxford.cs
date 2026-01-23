namespace DictionaryImporter.Sources.Common.Helper
{
    internal static class ParsingHelperOxford
    {
        public static IReadOnlyList<string> ExtractExamples(string definition)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return examples;

            var lines = definition.Split('\n');
            var inExamplesSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("【Examples】"))
                {
                    inExamplesSection = true;
                    continue;
                }

                if (inExamplesSection)
                {
                    if (trimmed.StartsWith("【") || string.IsNullOrEmpty(trimmed))
                        break;

                    if (trimmed.StartsWith("»"))
                        examples.Add(trimmed[1..].Trim());
                }
            }

            return examples;
        }

        public static IReadOnlyList<CrossReference> ExtractCrossReferences(string definition)
        {
            var crossRefs = new List<CrossReference>();

            var seeAlsoSection = SourceDataHelper.ExtractSection(definition, "【SeeAlso】");
            if (string.IsNullOrEmpty(seeAlsoSection))
                return crossRefs;

            var references = seeAlsoSection.Split(';', StringSplitOptions.RemoveEmptyEntries);

            crossRefs.AddRange(from refWord in references select refWord.Trim() into trimmed where !string.IsNullOrEmpty(trimmed) select new CrossReference { TargetWord = trimmed, ReferenceType = "SeeAlso" });

            return crossRefs;
        }

        // NEW COMPREHENSIVE OXFORD PARSING METHODS

        public static OxfordParsedData ParseOxfordEntry(string definition)
        {
            var data = new OxfordParsedData();

            if (string.IsNullOrWhiteSpace(definition))
                return data;

            data.Domain = ExtractOxfordDomain(definition);
            data.IpaPronunciation = ExtractIpaPronunciation(definition);
            data.PartOfSpeech = ExtractPartOfSpeech(definition);
            data.Variants = ExtractVariants(definition);
            data.UsageLabel = ExtractUsageLabel(definition);
            data.CleanDefinition = ExtractMainDefinition(definition);

            return data;
        }

        public static string? ExtractOxfordDomain(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            // Pattern 1: Look for Oxford-style domain/register labels in parentheses
            // Example: (informal, chiefly N. Amer.)
            var domainMatch = Regex.Match(definition, @"\(([^)]+)\)");
            if (domainMatch.Success)
            {
                var domain = domainMatch.Groups[1].Value.Trim();
                domain = CleanDomainText(domain);

                if (IsValidOxfordDomain(domain))
                {
                    return domain.Length <= 100 ? domain : domain.Substring(0, 100);
                }
            }

            // Pattern 2: Look for specific domain patterns
            var domainPatterns = new[]
            {
                @"\(informal, chiefly N\. Amer\.\)",
                @"\(chiefly N\. Amer\.\)",
                @"\(chiefly Brit\.\)",
                @"\(chiefly US\)",
                @"\(formal\)",
                @"\(informal\)",
                @"\(technical\)",
                @"\(literary\)",
                @"\(humorous\)",
                @"\(offensive\)",
                @"\(dated\)",
                @"\(archaic\)",
                @"\(rare\)",
                @"\(disputed\)",
                @"\(proscribed\)",
                @"\(slang\)",
                @"\(colloquial\)",
                @"\(dialect\)",
                @"\(regional\)"
            };

            foreach (var pattern in domainPatterns)
            {
                var match = Regex.Match(definition, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var domain = match.Value.Trim('(', ')', ' ').Replace(".", "");
                    domain = CleanDomainText(domain);

                    if (IsValidOxfordDomain(domain))
                    {
                        return domain.Length <= 100 ? domain : domain.Substring(0, 100);
                    }
                }
            }

            // Pattern 3: Check for 【Label】section (backward compatibility)
            var labelSection = SourceDataHelper.ExtractSection(definition, "【Label】");
            if (!string.IsNullOrWhiteSpace(labelSection))
            {
                var cleanLabel = CleanDomainText(labelSection);
                if (IsValidOxfordDomain(cleanLabel))
                {
                    return cleanLabel.Length <= 100 ? cleanLabel : cleanLabel.Substring(0, 100);
                }
            }

            return null;
        }

        public static string? ExtractIpaPronunciation(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            // Oxford IPA patterns:
            // 1. After headword: "24–7▶ (也作 24/7), adverb" - no IPA in this format
            // 2. In phonetic brackets: /ˈtwɛnti fɔː/
            // 3. With stress marks: /əˌkɒməˈdeɪʃn/

            var ipaPatterns = new[]
            {
                @"/([^/]+)/",  // Between slashes
                @"\[([^\]]+)\]", // Between brackets
                @"\s([ˈˌ][^/]+)/", // Starting with stress marks
            };

            foreach (var pattern in ipaPatterns)
            {
                var match = Regex.Match(definition, pattern);
                if (match.Success)
                {
                    var ipa = match.Groups[1].Value.Trim();
                    if (ContainsIpaCharacters(ipa))
                    {
                        return ipa.Length <= 50 ? ipa : ipa.Substring(0, 50);
                    }
                }
            }

            return null;
        }

        public static string? ExtractPartOfSpeech(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            // Oxford POS patterns:
            // 1. After comma: "24–7▶ (也作 24/7), adverb"
            // 2. In abbreviations: "n.", "v.", "adj.", etc.
            // 3. Full words: "noun", "verb", "adjective"

            var posPatterns = new[]
            {
                @",\s*(\w+)(?:\s|$)", // After comma
                @"^\s*([a-z]+\.)(?:\s|$)", // Abbreviation at start
                @"\b(noun|verb|adjective|adverb|pronoun|preposition|conjunction|interjection|determiner|numeral|article|particle)\b",
                @"\b(n\.|v\.|adj\.|adv\.|pron\.|prep\.|conj\.|interj\.|det\.|num\.|art\.|part\.)\b"
            };

            foreach (var pattern in posPatterns)
            {
                var match = Regex.Match(definition, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var pos = match.Groups[1].Value.Trim().ToLowerInvariant();

                    // Normalize abbreviations
                    pos = NormalizePartOfSpeech(pos);

                    return pos.Length <= 20 ? pos : pos.Substring(0, 20);
                }
            }

            return null;
        }

        public static IReadOnlyList<string> ExtractVariants(string definition)
        {
            var variants = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return variants;

            // Oxford variant patterns:
            // 1. Also作 markers: "24–7▶ (也作 24/7), adverb"
            // 2. Variants section: 【Variants】24/7, twenty-four seven

            var variantsSection = SourceDataHelper.ExtractSection(definition, "【Variants】");
            if (!string.IsNullOrWhiteSpace(variantsSection))
            {
                var variantList = variantsSection.Split(',', ';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var variant in variantList)
                {
                    var cleanVariant = variant.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanVariant))
                    {
                        variants.Add(cleanVariant);
                    }
                }
            }

            // Also extract from "也作" pattern
            var alsoPattern = @"也作\s*([^),]+)";
            var alsoMatch = Regex.Match(definition, alsoPattern);
            if (alsoMatch.Success)
            {
                var alsoVariants = alsoMatch.Groups[1].Value.Split(';', ',');
                foreach (var variant in alsoVariants)
                {
                    var cleanVariant = variant.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanVariant))
                    {
                        variants.Add(cleanVariant);
                    }
                }
            }

            return variants.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string? ExtractUsageLabel(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return null;

            // Oxford usage labels beyond domain:
            // 【Usage】, 【Grammar】, special markers

            var usageSection = SourceDataHelper.ExtractSection(definition, "【Usage】");
            if (!string.IsNullOrWhiteSpace(usageSection))
            {
                return usageSection.Trim().Length <= 50 ? usageSection.Trim() : usageSection.Trim().Substring(0, 50);
            }

            var grammarSection = SourceDataHelper.ExtractSection(definition, "【Grammar】");
            if (!string.IsNullOrWhiteSpace(grammarSection))
            {
                return grammarSection.Trim().Length <= 50 ? grammarSection.Trim() : grammarSection.Trim().Substring(0, 50);
            }

            return null;
        }

        public static string ExtractMainDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var domain = ExtractOxfordDomain(definition);
            return CleanOxfordDefinition(definition, domain);
        }

        public static string CleanOxfordDefinition(string definition, string? extractedDomain = null)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return string.Empty;

            var cleaned = definition;

            // Remove Oxford star markers
            cleaned = Regex.Replace(cleaned, @"^★+☆+\s*", "");

            // Remove ▶ marker and what follows (variants, etc.)
            cleaned = Regex.Replace(cleaned, @"▶.*?(?=,|\n|$)", "");

            // Remove sense number if at beginning
            cleaned = Regex.Replace(cleaned, @"^\d+\.\s*", "");

            // Remove extracted domain if provided
            if (!string.IsNullOrWhiteSpace(extractedDomain))
            {
                cleaned = Regex.Replace(cleaned, @"\([^)]*" + Regex.Escape(extractedDomain) + @"[^)]*\)", "");
            }

            // Remove any remaining parenthetical content (likely domain/register labels)
            cleaned = Regex.Replace(cleaned, @"\([^)]*\)", " ");

            // Remove IPA pronunciation if present
            cleaned = Regex.Replace(cleaned, @"/([^/]+)/", " ");
            cleaned = Regex.Replace(cleaned, @"\[([^\]]+)\]", " ");

            // Remove 【Sections】
            var sectionsToRemove = new[] { "【Label】", "【Variants】", "【Examples】", "【SeeAlso】", "【Usage】", "【Grammar】", "【Pronunciation】", "【Etymology】" };
            foreach (var section in sectionsToRemove)
            {
                cleaned = RemoveSection(cleaned, section);
            }

            // Extract English part before • separator (bilingual content)
            var parts = cleaned.Split('•', 2);
            var englishPart = parts[0].Trim();

            // Clean up whitespace and remove Oxford-specific punctuation
            englishPart = Regex.Replace(englishPart, @"[」「《》【】〖〗]|»|▶", " ");
            englishPart = Regex.Replace(englishPart, @"\s+", " ").Trim();

            // Remove trailing commas, colons
            englishPart = englishPart.TrimEnd(',', ';', ':', '.');

            return englishPart;
        }

        // HELPER METHODS

        private static string RemoveSection(string text, string sectionMarker)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(sectionMarker))
                return text;

            var index = text.IndexOf(sectionMarker);
            if (index >= 0)
            {
                // Find the end of the section (next section marker or end)
                var afterMarker = text.Substring(index + sectionMarker.Length);
                var endIndex = afterMarker.IndexOf("【");
                if (endIndex >= 0)
                {
                    return text.Substring(0, index) + afterMarker.Substring(endIndex);
                }
                return text.Substring(0, index).Trim();
            }

            return text;
        }

        private static string CleanDomainText(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return string.Empty;

            var cleaned = domain;

            // Remove Chinese characters
            cleaned = Regex.Replace(cleaned, @"[\u4e00-\u9fff]", "");

            // Normalize abbreviations
            cleaned = cleaned.Replace("N.", "N")
                            .Replace("Amer.", "Amer")
                            .Replace("Brit.", "Brit")
                            .Replace("US.", "US")
                            .Replace("UK.", "UK")
                            .Replace("esp.", "especially")
                            .Replace("chiefly", "mainly");

            // Remove trailing punctuation
            cleaned = cleaned.Trim().TrimEnd('.', ',', ';', ':');

            // Collapse multiple spaces
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        private static string NormalizePartOfSpeech(string pos)
        {
            return pos.ToLowerInvariant() switch
            {
                "n." or "n" => "noun",
                "v." or "v" => "verb",
                "adj." or "adj" or "a." => "adjective",
                "adv." or "adv" => "adverb",
                "pron." or "pron" => "pronoun",
                "prep." or "prep" => "preposition",
                "conj." or "conj" => "conjunction",
                "interj." or "interj" => "interjection",
                "det." or "det" => "determiner",
                "num." or "num" => "numeral",
                "art." or "art" => "article",
                "part." or "part" => "particle",
                _ => pos
            };
        }

        private static bool ContainsIpaCharacters(string text)
        {
            // Check for IPA characters
            var ipaPattern = @"[ˈˌːɑæəɛɪɔʊʌθðŋʃʒʤʧɡɜɒɫɾɹɻʲ̃]";
            return Regex.IsMatch(text, ipaPattern);
        }

        private static bool IsValidOxfordDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain) || domain.Length > 150)
                return false;

            // Reject if it contains definition-like text
            var definitionIndicators = new[]
            {
                "hours", "days", "weeks", "minutes", "seconds",
                "o'clock", "twenty-four", "seven days", "all the time",
                "definition", "means", "refer to", "indicating", "example",
                "used to", "describing", "referring", "denoting"
            };

            if (definitionIndicators.Any(ind =>
                domain.Contains(ind, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // Accept common Oxford domain/register patterns
            var validPatterns = new[]
            {
                @"informal",
                @"formal",
                @"technical",
                @"literary",
                @"humorous",
                @"offensive",
                @"dated",
                @"archaic",
                @"rare",
                @"disputed",
                @"proscribed",
                @"slang",
                @"colloquial",
                @"dialect",
                @"regional",
                @"chiefly",
                @"mainly",
                @"especially",
                @"particularly",
                @"primarily",
                @"originally",
                @"historically",
                @"traditionally"
            };

            return validPatterns.Any(pattern =>
                Regex.IsMatch(domain, pattern, RegexOptions.IgnoreCase));
        }
        public static IReadOnlyList<string>? ExtractSynonymsFromExamples(IReadOnlyList<string> examples)
        {
            if (examples == null || examples.Count == 0)
                return null;

            var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var synonymPatterns = new[]
            {
                @"\b(?:synonymous|synonym|same as|equivalent to|also called)\s+(?:[\w\s]*?\s)?(?<word>\b[A-Z][a-z]+\b)",
                @"\b(?<word>\b[A-Z][a-z]+\b)\s*\((?:also|syn|syn\.|synonym)\)",
                @"\b(?<word1>\b[A-Z][a-z]+\b)\s+or\s+(?<word2>\b[A-Z][a-z]+\b)\b"
            };

            foreach (var example in examples)
            {
                foreach (var pattern in synonymPatterns)
                {
                    var matches = Regex.Matches(example, pattern);

                    foreach (Match match in matches)
                    {
                        if (match.Groups["word"].Success)
                            synonyms.Add(match.Groups["word"].Value.ToLowerInvariant());

                        if (match.Groups["word1"].Success)
                            synonyms.Add(match.Groups["word1"].Value.ToLowerInvariant());

                        if (match.Groups["word2"].Success)
                            synonyms.Add(match.Groups["word2"].Value.ToLowerInvariant());
                    }
                }
            }

            return synonyms.Count > 0 ? synonyms.ToList() : null;
        }
    }
    public class OxfordParsedData
    {
        public string? Domain { get; set; }
        public string? IpaPronunciation { get; set; }
        public string? PartOfSpeech { get; set; }
        public IReadOnlyList<string> Variants { get; set; } = new List<string>();
        public string? UsageLabel { get; set; }
        public string CleanDefinition { get; set; } = string.Empty;
    }
}