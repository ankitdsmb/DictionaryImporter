namespace DictionaryImporter.Sources.Oxford.Parsing;

public static class OxfordSourceDataHelper
{
    public static bool TryParseHeadwordLine(
    string line,
    out string headword,
    out string? pronunciation,
    out string? partOfSpeech,
    out string? variantForms)
    {
        headword = string.Empty;
        pronunciation = null;
        partOfSpeech = null;
        variantForms = null;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var match = HeadwordRegex.Match(line);
        if (!match.Success)
            return false;

        headword = match.Groups["headword"].Value.Trim();
        // Remove trailing sense numbers (e.g., "word1." or "word 1")
        headword = Regex.Replace(headword, @"\s*\d+\.?$", "").Trim();

        // Also remove parenthesized numbers (e.g., "word (1)")
        headword = Regex.Replace(headword, @"\s*\(\d+\)$", "").Trim();

        var rest = match.Groups["rest"].Value;

        // Extract pronunciation (IPA in slashes)
        var pronMatch = PronunciationRegex.Match(rest);
        if (pronMatch.Success)
        {
            pronunciation = pronMatch.Value.Trim();
            rest = rest.Replace(pronMatch.Value, "").Trim();
        }

        // Extract variant forms
        var variantMatch = VariantFormsRegex.Match(rest);
        if (variantMatch.Success)
        {
            variantForms = variantMatch.Groups["variant"].Value.Trim();
            rest = rest.Replace(variantMatch.Value, "").Trim();
        }

        // Oxford ▶ adjective / noun / for abbreviation
        var blockPosMatch = BlockPartOfSpeechRegex.Match(rest);
        if (blockPosMatch.Success)
        {
            partOfSpeech = blockPosMatch.Groups["pos"].Value.Trim();
            rest = rest.Replace(blockPosMatch.Value, "").Trim();
        }
        else
        {
            // Try inline POS (comma-separated at end)
            var inlinePosMatch = PartOfSpeechRegex.Match(rest);
            if (inlinePosMatch.Success)
            {
                partOfSpeech = inlinePosMatch.Groups["pos"].Value.Trim();
                rest = rest.Replace(inlinePosMatch.Value, "").Trim();
            }
        }

        return true;
    }

    public static bool TryParseSenseLine(
        string line,
        out int senseNumber,
        out string? senseLabel,
        out string definition,
        out string? chineseTranslation)
    {
        senseNumber = 0;
        senseLabel = null;
        definition = string.Empty;
        chineseTranslation = null;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var senseMatch = SenseNumberRegex.Match(line);
        if (!senseMatch.Success)
            return false;

        senseNumber = int.Parse(senseMatch.Groups["number"].Value);
        var rest = senseMatch.Groups["rest"].Value.Trim();

        // Extract label in parentheses at the beginning
        var labelBuilder = new List<string>();

        while (true)
        {
            var labelMatch = SenseLabelRegex.Match(rest);
            if (!labelMatch.Success)
                break;

            var label = labelMatch.Groups["label"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(label))
                labelBuilder.Add(label);

            rest = labelMatch.Groups["rest"].Value.Trim();
        }

        if (labelBuilder.Count > 0)
            senseLabel = string.Join(", ", labelBuilder);

        // Extract Chinese translation (after bullet)
        var translationMatch = ChineseTranslationRegex.Match(rest);
        if (translationMatch.Success)
        {
            chineseTranslation = translationMatch.Groups["translation"].Value.Trim();
            rest = rest[..translationMatch.Index].Trim();
        }

        // Clean the definition
        definition = CleanDefinitionText(rest);

        return true;
    }

    private static string CleanDefinitionText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Remove Chinese characters
        text = Regex.Replace(text, @"[\u4e00-\u9fff]", "");

        // Remove Chinese punctuation
        text = Regex.Replace(text, @"[，。、；：！？【】（）《》〈〉「」『』]", "");

        // Remove Chinese translation markers like [of], [have]
        text = Regex.Replace(text, @"\[([A-Za-z]+)\]", "$1");

        // Remove empty brackets
        text = Regex.Replace(text, @"\[\s*\]", "");

        // Clean up whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    public static bool IsExampleLine(string line)
        => !string.IsNullOrWhiteSpace(line) && line.TrimStart().StartsWith("»", StringComparison.Ordinal);

    public static string CleanExampleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        line = line.TrimStart('»', ' ').Trim();

        // Remove Chinese text
        line = CleanDefinitionText(line);

        // Remove trailing source citations like [OALD]
        line = Regex.Replace(line, @"\s*\[[^\]]+\]$", "");

        return line.Trim();
    }

    public static bool IsEntrySeparator(string line)
        => !string.IsNullOrWhiteSpace(line) &&
           (line.StartsWith("────────", StringComparison.Ordinal) ||
            line.StartsWith("========", StringComparison.Ordinal) ||
            line.StartsWith("————————", StringComparison.Ordinal));

    public static IReadOnlyList<string> ExtractCrossReferences(string text)
    {
        var crossRefs = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return crossRefs;

        foreach (Match match in CrossReferenceRegex.Matches(text))
        {
            var word = match.Groups["word"].Value;
            if (!string.IsNullOrWhiteSpace(word))
                crossRefs.Add(word);
        }

        return crossRefs.Distinct().ToList();
    }

    public static string NormalizePartOfSpeech(string? rawPos)
    {
        if (string.IsNullOrWhiteSpace(rawPos))
            return "unk";

        var pos = rawPos.ToLowerInvariant().Trim();

        // Remove parenthetical qualifiers
        pos = Regex.Replace(pos, @"\s*\([^)]*\)", "").Trim();

        var posMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["noun"] = "noun",
            ["n"] = "noun",
            ["n."] = "noun",
            ["mass noun"] = "noun",
            ["count noun"] = "noun",
            ["uncountable noun"] = "noun",
            ["countable noun"] = "noun",
            ["plural noun"] = "noun",
            ["proper noun"] = "noun",

            ["verb"] = "verb",
            ["v"] = "verb",
            ["v."] = "verb",
            ["intransitive verb"] = "verb",
            ["transitive verb"] = "verb",
            ["linking verb"] = "verb",
            ["modal verb"] = "verb",
            ["phrasal verb"] = "verb",

            ["adjective"] = "adj",
            ["adj"] = "adj",
            ["adj."] = "adj",

            ["adverb"] = "adv",
            ["adv"] = "adv",
            ["adv."] = "adv",

            ["exclamation"] = "exclamation",
            ["interjection"] = "exclamation",

            ["for abbreviation"] = "abbreviation",
            ["abbreviation"] = "abbreviation",
            ["abbr"] = "abbreviation",
            ["abbr."] = "abbreviation",
            ["initialism"] = "abbreviation",
            ["acronym"] = "abbreviation",

            ["prefix"] = "prefix",
            ["suffix"] = "suffix",
            ["combining form"] = "combining form",

            ["numeral"] = "numeral",
            ["num"] = "numeral",
            ["num."] = "numeral",
            ["cardinal number"] = "numeral",
            ["ordinal number"] = "numeral",

            ["pronoun"] = "pronoun",
            ["pron"] = "pronoun",
            ["pron."] = "pronoun",

            ["conjunction"] = "conjunction",
            ["conj"] = "conjunction",
            ["conj."] = "conjunction",

            ["preposition"] = "preposition",
            ["prep"] = "preposition",
            ["prep."] = "preposition",

            ["determiner"] = "determiner",
            ["det"] = "determiner",
            ["det."] = "determiner",

            ["article"] = "article",
            ["definite article"] = "article",
            ["indefinite article"] = "article",

            ["idiom"] = "idiom",
            ["phrasal"] = "idiom",

            ["symbol"] = "symbol",
            ["sym"] = "symbol",
            ["sym."] = "symbol"
        };

        return posMap.TryGetValue(pos, out var normalized)
            ? normalized
            : "unk";
    }

    #region Compiled Regex Patterns

    private static readonly Regex HeadwordRegex =
        new(@"^★+☆+\s+(?<headword>[^\d▶]+?)(?:\s*\d+\.?)?\s*(?:▶\s*)?(?<rest>.*)$",
            RegexOptions.Compiled);

    private static readonly Regex PronunciationRegex =
        new(@"/[^/\n]+/", RegexOptions.Compiled);

    private static readonly Regex SenseNumberRegex =
        new(@"^(?<number>\d+)\.\s*(?<rest>.+)$", RegexOptions.Compiled);

    private static readonly Regex SenseLabelRegex =
        new(@"^\((?<label>[^)]+)\)\s*(?<rest>.+)$", RegexOptions.Compiled);

    private static readonly Regex ChineseTranslationRegex =
        new(@"•\s*(?<translation>.+)$", RegexOptions.Compiled);

    private static readonly Regex PartOfSpeechRegex =
        new(@",\s*(?<pos>\w+(?:\s+\w+)*?)$", RegexOptions.Compiled);

    private static readonly Regex BlockPartOfSpeechRegex =
        new(@"▶\s*(?<pos>for abbreviation|adjective|noun|verb|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|combining form|idiom|phrasal verb|symbol)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VariantFormsRegex =
        new(@"(?:\(|（)(?:also|也作|亦作|又作)\s*(?<variant>[^)）]+)(?:\)|）)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CrossReferenceRegex =
        new(@"\b(?:see|cf\.|compare|also|syn\.|synonym)\s+(?<word>\b[A-Z][A-Za-z\-']+\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    #endregion Compiled Regex Patterns
}