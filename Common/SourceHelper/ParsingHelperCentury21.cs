using System.Text.RegularExpressions;
using HtmlAgilityPack;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Common.SourceHelper;

internal static class ParsingHelperCentury21
{
    // =========================
    // MAIN PARSING METHOD FOR CENTURY21 HTML
    // =========================
    public static Century21ParsedData ParseCentury21Html(string htmlContent, string word)
    {
        var data = new Century21ParsedData();

        if (string.IsNullOrWhiteSpace(htmlContent))
            return data;

        try
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlContent);

            // Get all word blocks (there can be multiple for different senses)
            var wordBlocks = htmlDoc.DocumentNode.SelectNodes("//div[@class='word_block']");
            if (wordBlocks == null || wordBlocks.Count == 0)
                return data;

            // Parse first word block as main entry
            var firstBlock = wordBlocks[0];
            ParseBasicDefinition(firstBlock, data);

            // Parse variants if present
            var variantBlocks = firstBlock.SelectNodes(".//div[@class='variant']");
            if (variantBlocks != null)
            {
                foreach (var variant in variantBlocks)
                {
                    data.Variants.Add(ParseVariant(variant));
                }
            }

            // Parse idioms if present
            var idiomBlocks = firstBlock.SelectNodes(".//div[@class='idiom']");
            if (idiomBlocks != null)
            {
                foreach (var idiom in idiomBlocks)
                {
                    data.Idioms.Add(ParseIdiom(idiom));
                }
            }

            // Parse additional word blocks (different senses)
            if (wordBlocks.Count > 1)
            {
                for (int i = 1; i < wordBlocks.Count; i++)
                {
                    var additionalData = new Century21ParsedData();
                    ParseBasicDefinition(wordBlocks[i], additionalData);

                    // Add as variant since it's a different sense
                    if (!string.IsNullOrWhiteSpace(additionalData.Headword) &&
                        additionalData.Definitions.Any())
                    {
                        data.Variants.Add(new Century21Variant
                        {
                            Headword = additionalData.Headword,
                            PartOfSpeech = additionalData.PartOfSpeech,
                            Definitions = additionalData.Definitions.ToList(),
                            Examples = additionalData.Examples.ToList(),
                            DomainLabels = additionalData.DomainLabels,
                            RegisterLabels = additionalData.RegisterLabels
                        });
                    }
                }
            }

            // Clean up data - extract English-only content
            data.Definitions = ExtractEnglishDefinitions(data.Definitions);
            data.Examples = data.Examples.Where(e => IsPureEnglish(e)).Distinct().ToList();

            foreach (var variant in data.Variants)
            {
                variant.Definitions = ExtractEnglishDefinitions(variant.Definitions);
                variant.Examples = variant.Examples.Where(e => IsPureEnglish(e)).Distinct().ToList();
            }

            foreach (var idiom in data.Idioms)
            {
                idiom.Definition = ExtractEnglishDefinition(idiom.Definition);
                idiom.Examples = idiom.Examples.Where(e => IsPureEnglish(e)).Distinct().ToList();
            }

            // If no headword was found, use the provided word
            if (string.IsNullOrWhiteSpace(data.Headword) && !string.IsNullOrWhiteSpace(word))
            {
                data.Headword = word.Trim();
            }

            return data;
        }
        catch (Exception ex)
        {
            // Log error if needed
            return data;
        }
    }

    // =========================
    // PARSE BASIC DEFINITION
    // =========================
    private static void ParseBasicDefinition(HtmlNode wordBlock, Century21ParsedData data)
    {
        // Headword
        var headwordNode = wordBlock.SelectSingleNode(".//span[@class='headword']");
        if (headwordNode != null)
        {
            data.Headword = CleanEnglishText(headwordNode.InnerText.Trim());
        }

        // IPA Pronunciation
        var phoneticsNode = wordBlock.SelectSingleNode(".//span[@class='phonetics']");
        if (phoneticsNode != null)
        {
            var ipaText = phoneticsNode.InnerText.Trim();
            // Remove slashes if present
            ipaText = ipaText.Trim('/', '[', ']');
            data.IpaPronunciation = CleanEnglishText(ipaText);
        }

        // Part of Speech
        var posNode = wordBlock.SelectSingleNode(".//span[@class='pos']");
        if (posNode != null)
        {
            data.PartOfSpeech = NormalizeCentury21Pos(posNode.InnerText.Trim());
        }

        // Grammar/Usage Info - extract and map domain/register labels
        var gramNode = wordBlock.SelectSingleNode(".//span[@class='gram']");
        if (gramNode != null)
        {
            var gramText = gramNode.InnerText.Trim();
            data.GrammarInfo = CleanEnglishText(gramText);

            // Extract domain and register labels from grammar text
            data.DomainLabels = ExtractAndMapDomainLabels(gramText).ToList();
            data.RegisterLabels = ExtractAndMapRegisterLabels(gramText).ToList();
        }

        // Definitions
        var definitionNodes = wordBlock.SelectNodes(".//span[@class='definition']");
        if (definitionNodes != null)
        {
            foreach (var defNode in definitionNodes)
            {
                var definitionText = defNode.InnerText.Trim();
                var englishDefinition = ExtractEnglishDefinition(definitionText);

                if (!string.IsNullOrWhiteSpace(englishDefinition))
                {
                    data.Definitions.Add(englishDefinition);
                }
            }
        }

        // Examples
        var exampleNodes = wordBlock.SelectNodes(".//span[@class='ex_en']");
        if (exampleNodes != null)
        {
            foreach (var exNode in exampleNodes)
            {
                var exampleText = exNode.InnerText.Trim();
                if (IsPureEnglish(exampleText))
                {
                    data.Examples.Add(CleanEnglishText(exampleText));
                }
            }
        }
    }

    // =========================
    // PARSE VARIANT
    // =========================
    private static Century21Variant ParseVariant(HtmlNode variantBlock)
    {
        var variant = new Century21Variant();

        // Part of Speech for variant
        var posNode = variantBlock.SelectSingleNode(".//span[@class='pos']");
        if (posNode != null)
        {
            variant.PartOfSpeech = NormalizeCentury21Pos(posNode.InnerText.Trim());
        }

        // Grammar/Usage Info
        var gramNode = variantBlock.SelectSingleNode(".//span[@class='gram']");
        if (gramNode != null)
        {
            var gramText = gramNode.InnerText.Trim();
            variant.GrammarInfo = CleanEnglishText(gramText);

            // Extract domain and register labels from grammar text
            variant.DomainLabels = ExtractAndMapDomainLabels(gramText).ToList();
            variant.RegisterLabels = ExtractAndMapRegisterLabels(gramText).ToList();
        }

        // Definitions
        var definitionNodes = variantBlock.SelectNodes(".//span[@class='definition']");
        if (definitionNodes != null)
        {
            foreach (var defNode in definitionNodes)
            {
                var definitionText = defNode.InnerText.Trim();
                var englishDefinition = ExtractEnglishDefinition(definitionText);

                if (!string.IsNullOrWhiteSpace(englishDefinition))
                {
                    variant.Definitions.Add(englishDefinition);
                }
            }
        }

        // Examples
        var exampleNodes = variantBlock.SelectNodes(".//span[@class='ex_en']");
        if (exampleNodes != null)
        {
            foreach (var exNode in exampleNodes)
            {
                var exampleText = exNode.InnerText.Trim();
                if (IsPureEnglish(exampleText))
                {
                    variant.Examples.Add(CleanEnglishText(exampleText));
                }
            }
        }

        return variant;
    }

    // =========================
    // PARSE IDIOM
    // =========================
    private static Century21Idiom ParseIdiom(HtmlNode idiomBlock)
    {
        var idiom = new Century21Idiom();

        // Headword
        var headwordNode = idiomBlock.SelectSingleNode(".//span[@class='headword']");
        if (headwordNode != null)
        {
            idiom.Headword = CleanEnglishText(headwordNode.InnerText.Trim());
        }

        // Definition
        var definitionNode = idiomBlock.SelectSingleNode(".//span[@class='definition']");
        if (definitionNode != null)
        {
            var definitionText = definitionNode.InnerText.Trim();
            idiom.Definition = ExtractEnglishDefinition(definitionText);
        }

        // Examples
        var exampleNodes = idiomBlock.SelectNodes(".//span[@class='ex_en']");
        if (exampleNodes != null)
        {
            foreach (var exNode in exampleNodes)
            {
                var exampleText = exNode.InnerText.Trim();
                if (IsPureEnglish(exampleText))
                {
                    idiom.Examples.Add(CleanEnglishText(exampleText));
                }
            }
        }

        return idiom;
    }

    // =========================
    // EXTRACT AND MAP DOMAIN LABELS
    // =========================
    public static IReadOnlyList<string> ExtractAndMapDomainLabels(string text)
    {
        var domains = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return domains;

        // Extract Chinese domain markers with angle brackets
        var domainMatches = Regex.Matches(text, @"〈([^〉]+)〉");
        foreach (Match match in domainMatches)
        {
            var chineseDomain = match.Groups[1].Value.Trim();
            var mappedDomain = MapChineseDomainToEnglish(chineseDomain);

            if (!string.IsNullOrWhiteSpace(mappedDomain) && !domains.Contains(mappedDomain))
            {
                domains.Add(mappedDomain);
            }
        }

        // Also look for domain markers without brackets in the text
        var domainKeywords = new Dictionary<string, string>
        {
            { "植", "botany" },
            { "动", "zoology" },
            { "医", "medical" },
            { "化", "chemistry" },
            { "物", "physics" },
            { "数", "mathematics" },
            { "史", "history" },
            { "地", "geography" },
            { "商", "commerce" },
            { "经", "economics" },
            { "法", "law" },
            { "文", "literature" },
            { "语", "linguistics" },
            { "宗", "religion" },
            { "哲", "philosophy" },
            { "体", "sports" },
            { "音", "music" },
            { "美", "art" },
            { "农", "agriculture" },
            { "工", "engineering" },
            { "计", "computing" },
            { "生", "biology" },
            { "心", "psychology" }
        };

        foreach (var keyword in domainKeywords)
        {
            if (text.Contains(keyword.Key) && !domains.Contains(keyword.Value))
            {
                domains.Add(keyword.Value);
            }
        }

        return domains;
    }

    // =========================
    // EXTRACT AND MAP REGISTER LABELS
    // =========================
    public static IReadOnlyList<string> ExtractAndMapRegisterLabels(string text)
    {
        var registers = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return registers;

        // Map Chinese register markers
        var registerKeywords = new Dictionary<string, string>
        {
            { "BrE", "British" },
            { "AmE", "American" },
            { "〈口〉", "informal" },
            { "〈俗〉", "colloquial" },
            { "〈方〉", "dialect" },
            { "〈古〉", "archaic" },
            { "〈旧〉", "obsolete" },
            { "〈婉〉", "euphemistic" },
            { "〈贬〉", "derogatory" },
            { "〈褒〉", "appreciative" },
            { "〈讽〉", "ironic" },
            { "〈谑〉", "humorous" },
            { "〈书〉", "literary" },
            { "〈正式〉", "formal" },
            { "〈非正式〉", "informal" },
            { "〈俚〉", "slang" },
            { "〈忌〉", "taboo" }
        };

        foreach (var keyword in registerKeywords)
        {
            if (text.Contains(keyword.Key) && !registers.Contains(keyword.Value))
            {
                registers.Add(keyword.Value);
            }
        }

        // Also check for abbreviated forms
        if (text.Contains("(BrE.)") && !registers.Contains("British"))
            registers.Add("British");
        if (text.Contains("(AmE.)") && !registers.Contains("American"))
            registers.Add("American");

        return registers;
    }

    // =========================
    // EXTRACT DOMAIN (FOR BACKWARD COMPATIBILITY)
    // =========================
    public static string? ExtractDomain(HtmlNode wordBlock)
    {
        if (wordBlock == null)
            return null;

        // Look for grammar markers that might indicate domain
        var gramNode = wordBlock.SelectSingleNode(".//span[@class='gram']");
        if (gramNode != null)
        {
            var gramText = gramNode.InnerText.Trim();
            var domains = ExtractAndMapDomainLabels(gramText);
            return domains.FirstOrDefault();
        }

        return null;
    }

    // =========================
    // EXTRACT USAGE LABEL (FOR BACKWARD COMPATIBILITY)
    // =========================
    public static string? ExtractUsageLabel(HtmlNode wordBlock)
    {
        if (wordBlock == null)
            return null;

        var labels = new List<string>();

        // Part of Speech
        var posNode = wordBlock.SelectSingleNode(".//span[@class='pos']");
        if (posNode != null)
        {
            var pos = NormalizeCentury21Pos(posNode.InnerText.Trim());
            if (!string.IsNullOrWhiteSpace(pos))
                labels.Add(pos);
        }

        // Grammar/Usage Info
        var gramNode = wordBlock.SelectSingleNode(".//span[@class='gram']");
        if (gramNode != null)
        {
            var gramText = gramNode.InnerText.Trim();

            // Extract usage labels from grammar text
            var registerLabels = ExtractAndMapRegisterLabels(gramText);
            labels.AddRange(registerLabels);
        }

        return labels.Count > 0 ? string.Join(", ", labels) : null;
    }

    // =========================
    // MAPPING FUNCTIONS
    // =========================

    private static string MapChineseDomainToEnglish(string chineseDomain)
    {
        return chineseDomain switch
        {
            "植" => "botany",
            "动" => "zoology",
            "医" => "medical",
            "化" => "chemistry",
            "物" => "physics",
            "数" => "mathematics",
            "史" => "history",
            "地" => "geography",
            "商" => "commerce",
            "经" => "economics",
            "法" => "law",
            "文" => "literature",
            "语" => "linguistics",
            "宗" => "religion",
            "哲" => "philosophy",
            "体" => "sports",
            "音" => "music",
            "美" => "art",
            "农" => "agriculture",
            "工" => "engineering",
            "计" => "computing",
            "生" => "biology",
            "心" => "psychology",
            "海" => "nautical",
            "船" => "nautical",
            "航" => "aviation",
            "空" => "aviation",
            "军" => "military",
            "政" => "politics",
            "社" => "sociology",
            "教" => "education",
            "戏" => "drama",
            "影" => "film",
            "视" => "television",
            "报" => "journalism",
            "广" => "advertising",
            "贸" => "trade",
            "金" => "finance",
            "银" => "banking",
            "税" => "taxation",
            "保" => "insurance",
            "证" => "securities",
            "股" => "stocks",
            _ => chineseDomain // Return original if no mapping found
        };
    }

    // =========================
    // ENGLISH EXTRACTION HELPERS
    // =========================

    private static string ExtractEnglishDefinition(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var englishParts = new List<string>();

        // 1. Look for English in parentheses (most common in Century21)
        var parenMatches = Regex.Matches(text, @"\(([^)]+)\)");
        foreach (Match match in parenMatches)
        {
            var content = match.Groups[1].Value.Trim();
            if (IsPureEnglish(content))
            {
                englishParts.Add(content);
            }
        }

        // 2. Look for English abbreviations and acronyms
        var abbrevMatches = Regex.Matches(text, @"\b(?:[A-Z]{2,}(?:\s+[A-Z]{2,})*|[A-Z]\.[A-Z]\.(?:[A-Z]\.)?)\b");
        foreach (Match match in abbrevMatches)
        {
            var abbrev = match.Value.Trim();
            if (IsPureEnglish(abbrev))
            {
                englishParts.Add(abbrev);
            }
        }

        // 3. Look for English phrases that look like definitions
        var phrasePattern = @"\b(?:[A-Z][a-z]+(?:\s+(?:[a-z]+|[A-Z][a-z]*)){1,5})\b";
        var phraseMatches = Regex.Matches(text, phrasePattern);
        foreach (Match match in phraseMatches)
        {
            var phrase = match.Value.Trim();
            if (IsPureEnglish(phrase) && phrase.Split(' ').Length >= 2)
            {
                englishParts.Add(phrase);
            }
        }

        // Remove duplicates and common words
        var filteredParts = englishParts
            .Distinct()
            .Where(p => !IsCommonWord(p) && p.Length > 1)
            .ToList();

        return filteredParts.Count > 0 ? string.Join("; ", filteredParts) : string.Empty;
    }

    private static List<string> ExtractEnglishDefinitions(List<string> definitions)
    {
        var result = new List<string>();

        foreach (var definition in definitions)
        {
            var englishDef = ExtractEnglishDefinition(definition);
            if (!string.IsNullOrWhiteSpace(englishDef) && !result.Contains(englishDef))
            {
                result.Add(englishDef);
            }
        }

        return result;
    }

    private static bool HasEnglishContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return Regex.IsMatch(text, @"[A-Za-z]");
    }

    private static bool IsPureEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Must not contain Chinese characters
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
            return false;

        // Must not contain Chinese punctuation
        if (text.Contains('〈') || text.Contains('〉') ||
            text.Contains('《') || text.Contains('》') ||
            text.Contains('「') || text.Contains('」') ||
            text.Contains('。') || text.Contains('，') ||
            text.Contains('；') || text.Contains('：'))
            return false;

        // Must contain English letters
        return Regex.IsMatch(text, @"[A-Za-z]");
    }

    private static bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "of", "in", "to", "for", "on", "at", "by",
            "and", "or", "but", "is", "are", "was", "were", "be", "been",
            "have", "has", "had", "do", "does", "did", "will", "would",
            "can", "could", "should", "might", "may", "must", "one", "two",
            "that", "this", "these", "those", "it", "its", "they", "them",
            "their", "there", "here", "where", "when", "why", "how", "what",
            "which", "who", "whom", "whose"
        };

        return commonWords.Contains(word.Trim());
    }

    private static string CleanEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Remove HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Remove any remaining non-English characters
        text = Regex.Replace(text, @"[^\x00-\x7F]", " ");

        // Remove excessive punctuation
        text = Regex.Replace(text, @"\.{2,}", ".");
        text = Regex.Replace(text, @",{2,}", ",");
        text = Regex.Replace(text, @";{2,}", ";");

        return text.Trim();
    }

    private static string NormalizeCentury21Pos(string pos)
    {
        return pos.ToLowerInvariant().TrimEnd('.') switch
        {
            "pref" or "prefix" => "prefix",
            "abbr" or "abbreviation" => "abbreviation",
            "n" or "noun" => "noun",
            "v" or "verb" => "verb",
            "vt" or "transitive verb" => "verb_transitive",
            "vi" or "intransitive verb" => "verb_intransitive",
            "adj" or "adjective" => "adjective",
            "adv" or "adverb" => "adverb",
            "prep" or "preposition" => "preposition",
            "conj" or "conjunction" => "conjunction",
            "pron" or "pronoun" => "pronoun",
            "int" or "interj" or "interjection" => "interjection",
            "suf" or "suffix" => "suffix",
            _ => pos
        };
    }

    public static string CleanText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Normalize whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Decode HTML entities safely
        text = HtmlEntity.DeEntitize(text);

        return text.Trim();
    }
}

// =========================
// ENHANCED DATA MODELS FOR CENTURY21
// =========================
public class Century21ParsedData
{
    public string Headword { get; set; } = string.Empty;
    public string IpaPronunciation { get; set; } = string.Empty;
    public string PartOfSpeech { get; set; } = string.Empty;
    public string GrammarInfo { get; set; } = string.Empty;
    public List<string> Definitions { get; set; } = new List<string>();
    public List<string> Examples { get; set; } = new List<string>();
    public List<Century21Variant> Variants { get; set; } = new List<Century21Variant>();
    public List<Century21Idiom> Idioms { get; set; } = new List<Century21Idiom>();
    public List<string> DomainLabels { get; set; } = new List<string>();
    public List<string> RegisterLabels { get; set; } = new List<string>();
}

public class Century21Variant
{
    public string Headword { get; set; } = string.Empty;
    public string PartOfSpeech { get; set; } = string.Empty;
    public string GrammarInfo { get; set; } = string.Empty;
    public List<string> Definitions { get; set; } = new List<string>();
    public List<string> Examples { get; set; } = new List<string>();
    public List<string> DomainLabels { get; set; } = new List<string>();
    public List<string> RegisterLabels { get; set; } = new List<string>();
}

public class Century21Idiom
{
    public string Headword { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public List<string> Examples { get; set; } = new List<string>();
}