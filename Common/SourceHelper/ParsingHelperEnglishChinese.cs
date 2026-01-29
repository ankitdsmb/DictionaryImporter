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
        data.MainDefinition = ExtractEnglishDefinition(chineseText);

        // Extract examples - ONLY ENGLISH TEXT
        data.Examples = ExtractExamples(chineseText);

        // Extract additional senses - ONLY ENGLISH TEXT
        data.AdditionalSenses = ExtractAdditionalSenses(chineseText);
    }

    public static string? ExtractPartOfSpeech(string chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
            return null;

        // Look for POS patterns at the beginning
        var posPattern = @"^\s*(n\.|v\.|vt\.|vi\.|adj\.|a\.|adv\.|ad\.|prep\.|conj\.|pron\.|int\.|interj\.|abbr\.|phr\.|phrase|pl\.|sing\.|comb\.form|suffix|prefix|num\.|det\.|exclam\.)\b";
        var match = Regex.Match(chineseText, posPattern, RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var pos = match.Value.Trim().ToLowerInvariant();
            return NormalizeEngChnPartOfSpeech(pos);
        }

        return null;
    }

    public static IReadOnlyList<string> ExtractDomainLabels(string chineseText)
    {
        var domains = new List<string>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return domains;

        // Extract domain labels like 〔医〕, 〔农〕, 〔化〕
        var domainMatches = Regex.Matches(chineseText, @"〔([^〕]+)〕");
        foreach (Match match in domainMatches)
        {
            var domain = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(domain))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    public static IReadOnlyList<string> ExtractRegisterLabels(string chineseText)
    {
        var registers = new List<string>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return registers;

        // Extract register labels like 〈口〉, 〈美〉, 〈英〉, 〈正式〉
        var registerMatches = Regex.Matches(chineseText, @"〈([^〉]+)〉");
        foreach (Match match in registerMatches)
        {
            var register = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(register))
            {
                registers.Add(register);
            }
        }

        return registers;
    }

    public static string? ExtractEtymology(string chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
            return null;

        // Extract etymology marked with brackets
        var etymologyPattern = @"\[\s*(?:<|字面意义：)([^\]]+)\]";
        var match = Regex.Match(chineseText, etymologyPattern);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return null;
    }

    // FIXED: This method should return ONLY ENGLISH TEXT for the definition
    public static string ExtractEnglishDefinition(string chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
            return string.Empty;

        // Clean the text: remove domain labels, register labels, etymology, POS
        var cleaned = chineseText;

        // Remove domain labels
        cleaned = Regex.Replace(cleaned, @"〔[^〕]+〕", "");

        // Remove register labels
        cleaned = Regex.Replace(cleaned, @"〈[^〉]+〉", "");

        // Remove etymology
        cleaned = Regex.Replace(cleaned, @"\[\s*(?:<|字面意义：)[^\]]+\]", "");

        // Remove POS markers at the beginning
        var posPattern = @"^\s*\b(?:n\.|v\.|vt\.|vi\.|adj\.|a\.|adv\.|ad\.|prep\.|conj\.|pron\.|int\.|interj\.|abbr\.|phr\.|phrase|pl\.|sing\.|comb\.form|suffix|prefix|num\.|det\.|exclam\.)\b\.?\s*";
        cleaned = Regex.Replace(cleaned, posPattern, "", RegexOptions.IgnoreCase);

        // Remove Chinese text and extract ONLY ENGLISH
        var englishText = ExtractPureEnglishText(cleaned);

        // Clean up the English text
        englishText = CleanEnglishText(englishText);

        return englishText;
    }

    // NEW: Extract only pure English text from mixed content
    private static string ExtractPureEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = new System.Text.StringBuilder();

        // Strategy 1: Look for quoted English text
        var quoteMatches = Regex.Matches(text, @"[""']([^""']+)[""']");
        foreach (Match match in quoteMatches)
        {
            var content = match.Groups[1].Value.Trim();
            if (IsPureEnglish(content))
            {
                AddUniqueToResult(result, content);
            }
        }

        // Strategy 2: Look for English in parentheses
        var parenMatches = Regex.Matches(text, @"\(([^)]+)\)");
        foreach (Match match in parenMatches)
        {
            var content = match.Groups[1].Value.Trim();
            if (IsPureEnglish(content))
            {
                AddUniqueToResult(result, content);
            }
        }

        // Strategy 3: Look for English words/phrases
        // Match words starting with capital letters, abbreviations, numbers with letters, etc.
        var patternMatches = Regex.Matches(text, @"\b([A-Z][A-Za-z0-9\-/\.]*(?:\s+[A-Z][A-Za-z0-9\-/\.]*)*)\b");
        foreach (Match match in patternMatches)
        {
            var word = match.Groups[1].Value.Trim();
            if (IsPureEnglish(word) && word.Length > 1)
            {
                AddUniqueToResult(result, word);
            }
        }

        // Strategy 4: Look for common English patterns
        // Like "A5", "9/11", "24-7", "AA", "AAA", etc.
        var specialPatterns = new[]
        {
            @"\b(?:A\d+|AA\d*|AAA\d*|A-?\d+)\b",  // A5, A-1, AAA, etc.
            @"\b\d+[-/]\d+\b",                    // 9/11, 24/7
            @"\b(?:[A-Z]\.)+[A-Z]?\b",            // A.A., A.A.A., etc.
            @"\b(?:[a-zA-Z]+-?)+[a-zA-Z]+\b",     // hyphenated words
        };

        foreach (var pattern in specialPatterns)
        {
            var matches = Regex.Matches(text, pattern);
            foreach (Match match in matches)
            {
                var word = match.Value;
                if (IsPureEnglish(word))
                {
                    AddUniqueToResult(result, word);
                }
            }
        }

        return result.ToString().Trim();
    }

    private static void AddUniqueToResult(System.Text.StringBuilder result, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var cleanText = text.Trim();
        if (cleanText.Length == 0)
            return;

        // Don't add duplicates
        var current = result.ToString();
        if (!current.Contains(cleanText))
        {
            if (result.Length > 0)
                result.Append(" ");
            result.Append(cleanText);
        }
    }

    private static bool IsPureEnglish(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check if contains any Chinese characters
        if (ContainsChinese(text))
            return false;

        // Check for English letters, numbers, common punctuation
        foreach (char c in text)
        {
            if (!((c >= 'A' && c <= 'Z') ||
                  (c >= 'a' && c <= 'z') ||
                  (c >= '0' && c <= '9') ||
                  c == ' ' || c == '-' || c == '/' || c == '.' ||
                  c == '(' || c == ')' || c == '"' || c == '\''))
            {
                return false;
            }
        }

        return true;
    }

    public static IReadOnlyList<string> ExtractExamples(string chineseText)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return examples;

        // Split by sentences
        var sentences = chineseText.Split(new[] { '。', '；', '!', '?', '.', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();

            // Check if it's an English example sentence
            if (trimmed.Length > 10 &&
                char.IsUpper(trimmed[0]) &&
                IsPureEnglish(trimmed) &&
                !trimmed.Contains("例如") &&
                !trimmed.Contains("例句"))
            {
                examples.Add(CleanEnglishText(trimmed));
            }
        }

        return examples;
    }

    public static IReadOnlyList<EnglishChineseParsedData> ExtractAdditionalSenses(string chineseText)
    {
        var additionalSenses = new List<EnglishChineseParsedData>();

        if (string.IsNullOrWhiteSpace(chineseText))
            return additionalSenses;

        // Extract numbered senses: 1., 2., etc.
        var senseMatches = Regex.Matches(chineseText, @"(\d+)\.\s*(.+?)(?=(?:\d+\.|$))", RegexOptions.Singleline);

        for (int i = 0; i < senseMatches.Count; i++)
        {
            var match = senseMatches[i];
            var senseDefinition = match.Groups[2].Value.Trim();

            if (!string.IsNullOrWhiteSpace(senseDefinition))
            {
                // Extract ONLY ENGLISH from this sense
                var englishDefinition = ExtractPureEnglishText(senseDefinition);

                // Only add if we have actual English content
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

    // FIXED: Extract domain separately (not mixed with definition)
    public static string? ExtractDomain(string chineseText)
    {
        var domainLabels = ExtractDomainLabels(chineseText);
        if (domainLabels.Count > 0)
        {
            // Return first domain label
            return domainLabels.FirstOrDefault();
        }

        return null;
    }

    // FIXED: Extract usage label separately (not mixed with definition)
    public static string? ExtractUsageLabel(string chineseText)
    {
        var partOfSpeech = ExtractPartOfSpeech(chineseText);
        var registerLabels = ExtractRegisterLabels(chineseText);

        var usageParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(partOfSpeech))
            usageParts.Add(partOfSpeech);

        if (registerLabels.Count > 0)
        {
            // Add register labels to usage
            usageParts.AddRange(registerLabels);
        }

        return usageParts.Count > 0 ? string.Join(", ", usageParts) : null;
    }

    public static string ExtractChineseDefinition(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Handle ⬄ separator - extract the Chinese part
        if (text.Contains('⬄'))
        {
            var parts = text.Split('⬄', 2);
            if (parts.Length > 1)
            {
                return parts[1].Trim();
            }
        }

        // If no separator, return the text as is
        return text.Trim();
    }

    // HELPER METHODS
    private static string NormalizeEngChnPartOfSpeech(string pos)
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
            "pl" or "plural" => "plural",
            "sing" or "singular" => "singular",
            "comb" or "comb.form" or "combining form" => "combining_form",
            "suffix" => "suffix",
            "prefix" => "prefix",
            "num" or "numeral" => "numeral",
            "det" or "determiner" => "determiner",
            "exclam" or "exclamation" => "exclamation",
            _ => pos
        };
    }

    private static string CleanEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = text;

        // Remove excessive whitespace
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        // Remove trailing punctuation
        cleaned = cleaned.TrimEnd('。', '，', '；', '：', '？', '！', '.', ',', ';', ':', '!', '?');

        // Remove any non-English characters
        var englishOnly = new System.Text.StringBuilder();
        foreach (char c in cleaned)
        {
            if ((c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') ||
                (c >= '0' && c <= '9') ||
                c == ' ' || c == '-' || c == '/' || c == '.' ||
                c == '(' || c == ')' || c == '"' || c == '\'')
            {
                englishOnly.Append(c);
            }
        }

        cleaned = englishOnly.ToString().Trim();

        // Final cleanup
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned;
    }

    private static bool ContainsChinese(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (char c in text)
        {
            if (IsChineseCharacter(c) || IsChinesePunctuation(c))
                return true;
        }

        return false;
    }

    private static bool IsChinesePunctuation(char c)
    {
        var chinesePunctuation = new HashSet<char>
        {
            '〔', '〕', '【', '】', '（', '）', '《', '》',
            '「', '」', '『', '』', '〖', '〗', '〈', '〉',
            '。', '；', '，', '、', '・', '…', '‥', '—',
            '～', '・', '‧', '﹑', '﹒', '﹔', '﹕', '﹖',
            '﹗', '﹘', '﹙', '﹚', '﹛', '﹜', '﹝', '﹞'
        };

        return chinesePunctuation.Contains(c);
    }

    private static bool IsChineseCharacter(char c)
    {
        int code = (int)c;
        return (code >= 0x4E00 && code <= 0x9FFF) ||   // CJK Unified Ideographs
               (code >= 0x3400 && code <= 0x4DBF) ||   // CJK Extension A
               (code >= 0x20000 && code <= 0x2A6DF) || // CJK Extension B
               (code >= 0x2A700 && code <= 0x2B73F) || // CJK Extension C
               (code >= 0x2B740 && code <= 0x2B81F) || // CJK Extension D
               (code >= 0x2B820 && code <= 0x2CEAF);   // CJK Extension E
    }
}