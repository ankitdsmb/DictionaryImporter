using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Common.SourceHelper;

internal static class ParsingHelperEnglishChinese
{
    // MAIN PARSING METHOD
    public static EnglishChineseParsedData ParseEngChnEntry(string rawLine)
    {
        var data = new EnglishChineseParsedData();

        if (string.IsNullOrWhiteSpace(rawLine))
            return data;

        // FIRST: Decode HTML entities
        rawLine = System.Net.WebUtility.HtmlDecode(rawLine);

        // Split at ⬄ separator (English ⬄ Chinese)
        var parts = rawLine.Split('⬄', 2);
        if (parts.Length != 2)
        {
            // No separator, use entire line
            data.EnglishDefinition = rawLine.Trim();
            return data;
        }

        var englishSide = parts[0].Trim();
        var chineseSide = parts[1].Trim();

        // Extract from English side (headword, IPA, etc.)
        ParseEnglishSide(englishSide, data);

        // Extract from Chinese side (definition, domain, etc.)
        ParseChineseSide(chineseSide, data);

        return data;
    }

    private static void ParseEnglishSide(string englishText, EnglishChineseParsedData data)
    {
        if (string.IsNullOrWhiteSpace(englishText))
            return;

        // Extract headword - take first word before any special characters
        if (string.IsNullOrWhiteSpace(data.Headword))
        {
            // Extract the first word or symbol
            var headwordMatch = Regex.Match(englishText, @"^([A-Za-z0-9@\-\./,()]+)");
            if (headwordMatch.Success)
            {
                data.Headword = headwordMatch.Groups[1].Value.Trim();
            }
            else
            {
                // Fallback: take first non-space characters
                var firstPart = englishText.Split(new[] { ' ', '/', '〔', '〈', '[' }, 2)[0].Trim();
                if (!string.IsNullOrWhiteSpace(firstPart))
                {
                    data.Headword = firstPart;
                }
            }
        }

        // Extract IPA pronunciation (between slashes)
        var ipaMatch = Regex.Match(englishText, @"/([^/]+)/");
        if (ipaMatch.Success)
        {
            data.IpaPronunciation = ipaMatch.Groups[1].Value.Trim();
        }

        // Extract syllabification (words with dots: e.g., "ad·ven·ture")
        var syllabMatch = Regex.Match(englishText, @"([a-zA-Z]+(?:\·[a-zA-Z]+)+)");
        if (syllabMatch.Success)
        {
            data.Syllabification = syllabMatch.Groups[1].Value.Trim();
        }
    }

    private static void ParseChineseSide(string chineseText, EnglishChineseParsedData data)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
            return;

        // Extract POS (part of speech)
        data.PartOfSpeech = ExtractPartOfSpeech(chineseText);

        // Extract domain labels (〔医〕, 〔农〕, etc.)
        data.DomainLabels = ExtractDomainLabels(chineseText);

        // Extract register labels (〈口〉, 〈美〉, etc.)
        data.RegisterLabels = ExtractRegisterLabels(chineseText);

        // Extract etymology
        data.Etymology = ExtractEtymology(chineseText);

        // Extract main definition - ONLY ENGLISH TEXT
        data.MainDefinition = ExtractEnglishDefinitionOnly(chineseText);

        // Extract examples - ONLY ENGLISH TEXT
        data.Examples = ExtractExamples(chineseText);

        // Extract additional senses - ONLY ENGLISH TEXT
        data.AdditionalSenses = ExtractAdditionalSenses(chineseText);
    }

    // NEW: Extract only English definition text (fixed)
    public static string ExtractEnglishDefinitionOnly(string chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
            return string.Empty;

        // Remove all Chinese markers and text
        var englishParts = new List<string>();

        // 1. Extract content in parentheses (often English)
        var parenMatches = Regex.Matches(chineseText, @"\(([^)]+)\)");
        foreach (Match match in parenMatches)
        {
            var content = match.Groups[1].Value.Trim();
            if (Helper.IsPureEnglish(content))
            {
                englishParts.Add(content);
            }
        }

        // 2. Extract content after = sign
        var equalsMatches = Regex.Matches(chineseText, @"＝\s*([^。，；]+)");
        foreach (Match match in equalsMatches)
        {
            var content = match.Groups[1].Value.Trim();
            if (Helper.IsPureEnglish(content))
            {
                englishParts.Add(content);
            }
        }

        // 3. Extract standalone English words
        var wordPattern = @"\b(?:[A-Z][a-zA-Z0-9]*\s*)+";
        var wordMatches = Regex.Matches(chineseText, wordPattern);
        foreach (Match match in wordMatches)
        {
            var word = match.Value.Trim();
            if (Helper.IsPureEnglish(word) && word.Length > 1)
            {
                englishParts.Add(word);
            }
        }

        // Remove duplicates and join
        var uniqueParts = englishParts.Distinct().Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return string.Join("; ", uniqueParts);
    }

    // UPDATED: Extract part of speech with proper mapping
    public static string? ExtractPartOfSpeech(string chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
            return null;

        // Look for English POS patterns
        var englishPosPattern = @"^\s*(n\.|v\.|vt\.|vi\.|adj\.|a\.|adv\.|ad\.|prep\.|conj\.|pron\.|int\.|interj\.|abbr\.|phr\.|phrase)\b";
        var match = Regex.Match(chineseText, englishPosPattern, RegexOptions.IgnoreCase);

        if (match.Success)
        {
            return NormalizePartOfSpeech(match.Value.Trim().ToLowerInvariant());
        }

        // Check for Chinese POS markers
        if (chineseText.Contains("名") || chineseText.StartsWith("名"))
            return "noun";
        if (chineseText.Contains("动") || chineseText.Contains("动词"))
            return "verb";
        if (chineseText.Contains("形") || chineseText.Contains("形容词"))
            return "adjective";
        if (chineseText.Contains("副") || chineseText.Contains("副词"))
            return "adverb";
        if (chineseText.Contains("介") || chineseText.Contains("介词"))
            return "preposition";
        if (chineseText.Contains("连") || chineseText.Contains("连词"))
            return "conjunction";
        if (chineseText.Contains("代") || chineseText.Contains("代词"))
            return "pronoun";
        if (chineseText.Contains("叹") || chineseText.Contains("叹词"))
            return "interjection";
        if (chineseText.Contains("缩") || chineseText.Contains("缩写"))
            return "abbreviation";

        return null;
    }

    // UPDATED: Extract domain labels with mapping
    public static IReadOnlyList<string> ExtractDomainLabels(string chineseText)
    {
        var domains = new List<string>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return domains;

        // Extract domain labels like 〔医〕, 〔农〕, 〔化〕
        var domainMatches = Regex.Matches(chineseText, @"〔([^〕]+)〕");
        foreach (Match match in domainMatches)
        {
            var chineseDomain = match.Groups[1].Value.Trim();
            var englishDomain = MapChineseDomainToEnglish(chineseDomain);

            if (!string.IsNullOrWhiteSpace(englishDomain) && !domains.Contains(englishDomain))
            {
                domains.Add(englishDomain);
            }
        }

        return domains;
    }

    // UPDATED: Extract register labels with mapping
    public static IReadOnlyList<string> ExtractRegisterLabels(string chineseText)
    {
        var registers = new List<string>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return registers;

        // Extract register labels like 〈口〉, 〈美〉, 〈英〉, 〈正式〉
        var registerMatches = Regex.Matches(chineseText, @"〈([^〉]+)〉");
        foreach (Match match in registerMatches)
        {
            var chineseRegister = match.Groups[1].Value.Trim();
            var englishRegister = MapChineseRegisterToEnglish(chineseRegister);

            if (!string.IsNullOrWhiteSpace(englishRegister) && !registers.Contains(englishRegister))
            {
                registers.Add(englishRegister);
            }
        }

        return registers;
    }

    // Domain mapping
    private static string MapChineseDomainToEnglish(string chineseDomain)
    {
        return chineseDomain switch
        {
            "医" => "medical",
            "化" => "chemistry",
            "农" => "agriculture",
            "物" => "physics",
            "数" => "mathematics",
            "生" => "biology",
            "史" => "history",
            "地" => "geography",
            "商" => "commerce",
            "体" => "sports",
            "牌" => "cards",
            "纹章学" => "heraldry",
            _ => chineseDomain
        };
    }

    // Register mapping
    private static string MapChineseRegisterToEnglish(string chineseRegister)
    {
        return chineseRegister switch
        {
            "口" => "informal",
            "美" => "American",
            "英" => "British",
            "正式" => "formal",
            "俚" => "slang",
            "古" => "archaic",
            "方" => "dialect",
            "苏格兰" => "Scottish",
            "拉" => "Latin",
            _ => chineseRegister
        };
    }

    // EXISTING METHOD: Extract etymology (unchanged)
    public static string? ExtractEtymology(string chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
            return null;

        var etymologyPattern = @"\[\s*(?:<|字面意义：)([^\]]+)\]";
        var match = Regex.Match(chineseText, etymologyPattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    // UPDATED: Extract examples - only English
    public static IReadOnlyList<string> ExtractExamples(string chineseText)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return examples;

        // Look for English sentences
        var sentencePattern = @"([A-Z][^。!?]*[.!?])";
        var matches = Regex.Matches(chineseText, sentencePattern);

        foreach (Match match in matches)
        {
            var sentence = match.Value.Trim();
            if (Helper.IsPureEnglish(sentence) && sentence.Split(' ').Length > 1)
            {
                examples.Add(CleanEnglishText(sentence));
            }
        }

        return examples;
    }

    // UPDATED: Extract numbered senses
    public static IReadOnlyList<EnglishChineseParsedData> ExtractAdditionalSenses(string chineseText)
    {
        var additionalSenses = new List<EnglishChineseParsedData>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return additionalSenses;

        // Extract numbered senses
        var senseMatches = Regex.Matches(chineseText, @"(\d+)\.\s*(.+?)(?=(?:\d+\.|$))", RegexOptions.Singleline);

        for (int i = 0; i < senseMatches.Count; i++)
        {
            var match = senseMatches[i];
            var senseDefinition = match.Groups[2].Value.Trim();

            if (!string.IsNullOrWhiteSpace(senseDefinition))
            {
                var englishDefinition = ExtractEnglishDefinitionOnly(senseDefinition);
                if (!string.IsNullOrWhiteSpace(englishDefinition))
                {
                    var senseData = new EnglishChineseParsedData
                    {
                        MainDefinition = CleanEnglishText(englishDefinition),
                        SenseNumber = i + 1
                    };
                    additionalSenses.Add(senseData);
                }
            }
        }

        return additionalSenses;
    }

    // BACKWARD COMPATIBILITY METHODS

    // Method called by EnglishChineseParser.cs line 79
    public static string? ExtractDomain(string chineseText)
    {
        var domains = ExtractDomainLabels(chineseText);
        return domains.Count > 0 ? domains.FirstOrDefault() : null;
    }

    // Method called by EnglishChineseParser.cs line 82
    public static string? ExtractUsageLabel(string chineseText)
    {
        var usageParts = new List<string>();

        var pos = ExtractPartOfSpeech(chineseText);
        if (!string.IsNullOrWhiteSpace(pos))
            usageParts.Add(pos);

        var registers = ExtractRegisterLabels(chineseText);
        if (registers.Count > 0)
            usageParts.AddRange(registers);

        return usageParts.Count > 0 ? string.Join(", ", usageParts) : null;
    }

    // Method called by EnglishChineseEtymologyExtractor.cs line 113
    public static string ExtractChineseDefinition(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (text.Contains('⬄'))
        {
            var parts = text.Split('⬄', 2);
            if (parts.Length > 1)
            {
                return parts[1].Trim();
            }
        }

        return text.Trim();
    }

    // HELPER METHODS

    private static string NormalizePartOfSpeech(string pos)
    {
        return pos.ToLowerInvariant().TrimEnd('.') switch
        {
            "n" or "noun" => "noun",
            "v" or "verb" => "verb",
            "vt" or "transitive verb" => "verb_transitive",
            "vi" or "intransitive verb" => "verb_intransitive",
            "a" or "adj" or "adjective" => "adjective",
            "ad" or "adv" or "adverb" => "adverb",
            "prep" or "preposition" => "preposition",
            "conj" or "conjunction" => "conjunction",
            "pron" or "pronoun" => "pronoun",
            "int" or "interj" or "interjection" => "interjection",
            "abbr" or "abbreviation" => "abbreviation",
            "phr" or "phrase" => "phrase",
            _ => pos
        };
    }

    private static string CleanEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = text;

        // Normalize whitespace
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        // Remove trailing punctuation
        cleaned = cleaned.TrimEnd('.', ',', ';', ':', '!', '?');

        return cleaned;
    }
}