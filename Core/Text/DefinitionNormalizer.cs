namespace DictionaryImporter.Core.Text;

public sealed class DefinitionNormalizer : IDefinitionNormalizer
{
    private static readonly Regex[] _senseSplitters = new[]
    {
        new Regex(@"\n\s*[0-9]+[\.\)]\s*", RegexOptions.Compiled),      // "1.", "2)", etc.
        new Regex(@"\n\s*[a-z][\.\)]\s*", RegexOptions.Compiled),       // "a.", "b)", etc.
        new Regex(@"\n\s*•\s*", RegexOptions.Compiled),                 // Bullet points
        new Regex(@"\n\s*[\-\–\—]\s*", RegexOptions.Compiled),          // Dashes
        new Regex(@"\n\s*\*\s*", RegexOptions.Compiled),                // Asterisks
        new Regex(@";\s*(?=[A-Z0-9])", RegexOptions.Compiled),          // Semicolons before capital letters/numbers
    };

    private static readonly Regex[] _removePatterns = new[]
    {
        new Regex(@"^\s*[0-9]+[\.\)]\s*", RegexOptions.Compiled),      // Leading "1.", "2)"
        new Regex(@"^\s*[a-z][\.\)]\s*", RegexOptions.Compiled),       // Leading "a.", "b)"
        new Regex(@"^\s*•\s*", RegexOptions.Compiled),                 // Leading bullet
        new Regex(@"^\s*[\-\–\—]\s*", RegexOptions.Compiled),          // Leading dash
        new Regex(@"^\s*\*\s*", RegexOptions.Compiled),                // Leading asterisk
        new Regex(@"\s*\[[^\]]*\]\s*", RegexOptions.Compiled),         // Bracketed content (e.g., [archaic])
        new Regex(@"\s*\([^)]*\)\s*", RegexOptions.Compiled),          // Parenthesized content
        new Regex(@"\s*\{[^}]*\}\s*", RegexOptions.Compiled),          // Curly braced content
        new Regex(@"\b(cf\.|i\.e\.|e\.g\.|etc\.|viz\.)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    public string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        // 1) Normalize whitespace and line endings
        raw = NormalizeWhitespace(raw);

        // 2) Check if this looks like a single definition or multiple senses
        if (ShouldSplitIntoSenses(raw))
        {
            return ProcessMultipleSenses(raw);
        }

        // 3) Single definition cleanup
        return CleanupSingleDefinition(raw);
    }

    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Normalize all whitespace sequences to single spaces
        text = Regex.Replace(text, @"\s+", " ");

        // Normalize quotes
        text = text.Replace("“", "\"").Replace("”", "\"")
                   .Replace("‘", "'").Replace("’", "'")
                   .Replace("`", "'");

        // Fix spacing around punctuation
        text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");
        text = Regex.Replace(text, @"([(\[])\s+", "$1");
        text = Regex.Replace(text, @"\s+([)\]])", "$1");
        text = Regex.Replace(text, @"\s+([/\\])\s+", "$1");

        // Remove excessive punctuation
        text = Regex.Replace(text, @";\s*;", ";");
        text = Regex.Replace(text, @"\.\s*\.", ".");
        text = Regex.Replace(text, @",\s*,", ",");

        return text.Trim();
    }

    private static bool ShouldSplitIntoSenses(string text)
    {
        // Check for obvious multi-sense indicators
        if (Regex.IsMatch(text, @"\n\s*[0-9]+[\.\)]") ||
            Regex.IsMatch(text, @"\n\s*[a-z][\.\)]") ||
            Regex.IsMatch(text, @"\n\s*•") ||
            Regex.IsMatch(text, @"\n\s*[\-\–\—]") ||
            Regex.IsMatch(text, @"^[0-9]+[\.\)].*[0-9]+[\.\)]", RegexOptions.Multiline))
        {
            return true;
        }

        // Check for multiple semi-colon separated definitions that look like separate senses
        var semicolonCount = text.Count(c => c == ';');
        if (semicolonCount >= 2)
        {
            // Check if the text after semicolons looks like new definitions
            var parts = text.Split(';');
            if (parts.Length > 2)
            {
                int capitalStarts = parts.Skip(1).Count(p => p.Trim().Length > 0 && char.IsUpper(p.Trim()[0]));
                return capitalStarts >= parts.Length / 2; // At least half start with capital
            }
        }

        return false;
    }

    private string ProcessMultipleSenses(string raw)
    {
        var senses = ExtractSenses(raw);

        if (senses.Count == 0)
            return CleanupSingleDefinition(raw);

        if (senses.Count == 1)
            return CleanupSingleDefinition(senses[0]);

        // Clean and number each sense
        var cleanedSenses = new List<string>();
        for (int i = 0; i < senses.Count; i++)
        {
            var sense = CleanSense(senses[i], i + 1);
            if (!string.IsNullOrWhiteSpace(sense) && sense.Length >= 3)
            {
                cleanedSenses.Add($"{i + 1}) {sense}");
            }
        }

        if (cleanedSenses.Count == 0)
            return CleanupSingleDefinition(raw);

        if (cleanedSenses.Count == 1)
            return cleanedSenses[0].Substring(cleanedSenses[0].IndexOf(')') + 2).Trim();

        return string.Join("\n", cleanedSenses);
    }

    private List<string> ExtractSenses(string text)
    {
        var senses = new List<string>();

        // Try each splitter pattern
        foreach (var splitter in _senseSplitters)
        {
            if (splitter.IsMatch(text))
            {
                var parts = splitter.Split(text);
                if (parts.Length > 1)
                {
                    // First part might be preamble (like "n." or "v.")
                    bool hasPreamble = parts[0].Trim().Length < 50 &&
                                      (parts[0].Contains('.') || parts[0].Length < 20);

                    for (int i = hasPreamble ? 1 : 0; i < parts.Length; i++)
                    {
                        var part = parts[i].Trim();
                        if (!string.IsNullOrWhiteSpace(part))
                        {
                            senses.Add(part);
                        }
                    }
                    break;
                }
            }
        }

        // If no splitters worked, try manual split
        if (senses.Count == 0)
        {
            // Check for numbered list without line breaks
            var match = Regex.Match(text, @"([0-9]+[\.\)]\s*[^0-9]+(?:\s+[0-9]+[\.\)]|$))");
            if (match.Success)
            {
                var matches = Regex.Matches(text, @"([0-9]+[\.\)]\s*[^0-9]+)");
                foreach (Match m in matches)
                {
                    senses.Add(m.Groups[1].Value.Trim());
                }
            }
            else
            {
                // Fall back to semicolon splitting for long texts
                if (text.Length > 100 && text.Contains(';'))
                {
                    var parts = text.Split(';')
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();

                    if (parts.Count > 1)
                        senses.AddRange(parts);
                }
            }
        }

        return senses;
    }

    private string CleanSense(string sense, int senseNumber)
    {
        // Apply removal patterns
        var cleaned = sense;
        foreach (var pattern in _removePatterns)
        {
            cleaned = pattern.Replace(cleaned, " ");
        }

        // Remove duplicate words (common in dictionary definitions)
        cleaned = RemoveDuplicateWords(cleaned);

        // Clean up punctuation
        cleaned = Regex.Replace(cleaned, @"\s+([,.;:!?])", "$1");
        cleaned = Regex.Replace(cleaned, @"([,.;:])\s*$", ".");
        cleaned = Regex.Replace(cleaned, @"\s+", " ");

        // Ensure it starts with capital letter and ends with period
        cleaned = cleaned.Trim();
        if (cleaned.Length > 0)
        {
            if (char.IsLower(cleaned[0]))
            {
                cleaned = char.ToUpper(cleaned[0]) + cleaned.Substring(1);
            }

            if (!".!?:;".Contains(cleaned[cleaned.Length - 1]))
            {
                cleaned += ".";
            }
        }

        return cleaned;
    }

    private static string CleanupSingleDefinition(string text)
    {
        text = NormalizeWhitespace(text);

        // Remove common dictionary markers
        text = Regex.Replace(text, @"^\s*(n\.|v\.|adj\.|adv\.|prep\.|conj\.|interj\.|pron\.)\s*", "",
            RegexOptions.IgnoreCase);

        // Remove parenthesized examples and notes
        text = Regex.Replace(text, @"\s*\([^)]*\)\s*", " ");
        text = Regex.Replace(text, @"\s*\[[^\]]*\]\s*", " ");

        // Normalize to single sentence
        text = Regex.Replace(text, @";\s*", "; ");
        text = text.Trim();

        if (text.Length > 0)
        {
            // Ensure proper capitalization and punctuation
            if (char.IsLower(text[0]))
                text = char.ToUpper(text[0]) + text.Substring(1);

            if (!".!?:;".Contains(text[text.Length - 1]))
                text += ".";
        }

        return text;
    }

    private static string RemoveDuplicateWords(string text)
    {
        var words = text.Split(new[] { ' ', ',', ';', '.' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();
        var lastWord = "";

        foreach (var word in words)
        {
            var cleanWord = Regex.Replace(word, @"[^\w']", "").ToLower();
            if (cleanWord != lastWord || cleanWord.Length > 5) // Don't remove short duplicates
            {
                result.Append(word);
                result.Append(' ');
                lastWord = cleanWord;
            }
        }

        return result.ToString().Trim();
    }

    // Helper method for specific dictionary formats
    public string NormalizeForSource(string raw, string sourceType)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        // Apply source-specific preprocessing
        raw = sourceType?.ToLower() switch
        {
            "ENG_CHN" => PreprocessOxford(raw),
            "ENG_OXFORD" => PreprocessCollins(raw),
            "GUT_WEBSTER" => PreprocessGutenbergWebster(raw),
            "ENG_COLLINS" => PreprocessEnglishChinese(raw),
            "STRUCT_JSON" => PreprocessCentury21(raw),
            "CENTURY21" => PreprocessKaikki(raw),
            _ => raw
        };

        return Normalize(raw);
    }

    private string PreprocessOxford(string text)
    {
        // Remove Oxford-specific markers
        text = Regex.Replace(text, @"\b(Oxford|OED|OE|ME)\b\.?\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[.*?\]", " "); // Remove pronunciation guides
        return text;
    }

    private string PreprocessCollins(string text)
    {
        // Remove Collins-specific formatting
        text = Regex.Replace(text, @"(?<=\b\w)\/(?=\w\b)", " or "); // a/b -> a or b
        text = Regex.Replace(text, @"\bCOBUILD\b\s*", "", RegexOptions.IgnoreCase);
        return text;
    }

    private string PreprocessGutenbergWebster(string text)
    {
        // Webster specific cleanup
        text = Regex.Replace(text, @"\bWebster\b[''s]*\s*", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(Def|Defn|Definition)\s*\d*[\.:]?\s*", "", RegexOptions.IgnoreCase);
        return text;
    }

    private string PreprocessEnglishChinese(string text)
    {
        // Handle bilingual dictionary format
        text = Regex.Replace(text, @"【.*?】", " "); // Remove Chinese brackets
        text = Regex.Replace(text, @"\/\s*", "; "); // Convert slashes to semicolons
        return text;
    }

    private string PreprocessCentury21(string text)
    {
        // Century 21 specific
        text = Regex.Replace(text, @"\bC21\b\s*", "", RegexOptions.IgnoreCase);
        return text;
    }

    private string PreprocessKaikki(string text)
    {
        // Kaikki.org JSON format cleanup
        text = Regex.Replace(text, @"\{\{.*?\}\}", " "); // Remove wiki templates
        text = Regex.Replace(text, @"\[\[.*?\]\]", " "); // Remove wiki links
        text = Regex.Replace(text, @"'''", ""); // Remove bold markers
        text = Regex.Replace(text, @"''", ""); // Remove italic markers
        return text;
    }
}