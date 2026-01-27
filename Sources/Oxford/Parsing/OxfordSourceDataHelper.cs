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

        // Extract pronunciation (IPA in slashes) - FIXED to handle multiple pronunciations
        var pronMatches = PronunciationRegex.Matches(rest);
        if (pronMatches.Count > 0)
        {
            var pronList = new List<string>();
            foreach (Match pronMatch in pronMatches)
            {
                var pron = pronMatch.Value.Trim();
                // Clean Chinese text from pronunciation
                pron = Regex.Replace(pron, @"[\u4e00-\u9fff].*$", "").Trim();
                if (!string.IsNullOrWhiteSpace(pron))
                    pronList.Add(pron);
            }
            if (pronList.Count > 0)
                pronunciation = string.Join("; ", pronList);
            // Remove pronunciation from rest
            rest = PronunciationRegex.Replace(rest, "").Trim();
        }

        // Extract variant forms - FIXED to handle Chinese variants
        var variantMatch = VariantFormsRegex.Match(rest);
        if (variantMatch.Success)
        {
            variantForms = variantMatch.Groups["variant"].Value.Trim();
            // Clean Chinese text from variants
            variantForms = Regex.Replace(variantForms, @"[\u4e00-\u9fff].*$", "").Trim();
            variantForms = Regex.Replace(variantForms, @"\s*[，；].*$", "").Trim(); // Remove Chinese punctuation and following text
            rest = rest.Replace(variantMatch.Value, "").Trim();
        }

        // Oxford ▶ adjective / noun / for abbreviation - FIXED to properly capture POS
        var blockPosMatch = BlockPartOfSpeechRegex.Match(rest);
        if (blockPosMatch.Success)
        {
            partOfSpeech = blockPosMatch.Groups["pos"].Value.Trim();
            // Clean any Chinese text from POS
            partOfSpeech = Regex.Replace(partOfSpeech, @"[\u4e00-\u9fff].*$", "").Trim();
            rest = rest.Replace(blockPosMatch.Value, "").Trim();
        }
        else
        {
            // Try inline POS (comma-separated at end)
            var inlinePosMatch = PartOfSpeechRegex.Match(rest);
            if (inlinePosMatch.Success)
            {
                partOfSpeech = inlinePosMatch.Groups["pos"].Value.Trim();
                // Clean any Chinese text from POS
                partOfSpeech = Regex.Replace(partOfSpeech, @"[\u4e00-\u9fff].*$", "").Trim();
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
        // Can be multi-level: "(informal, mass noun)" or "(informal) (mass noun)"
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

        // Extract Chinese translation (after bullet) - FIXED to better handle Chinese text
        var translationMatch = ChineseTranslationRegex.Match(rest);
        if (translationMatch.Success)
        {
            chineseTranslation = translationMatch.Groups["translation"].Value.Trim();
            // Clean up the translation
            chineseTranslation = Regex.Replace(chineseTranslation, @"^[•\s]*", "").Trim();
            // Remove any English text that might have been captured
            chineseTranslation = Regex.Replace(chineseTranslation, @"[A-Za-z].*$", "").Trim();
            rest = rest[..translationMatch.Index].Trim();
        }

        // Clean the definition - remove any Chinese text that might be at the end
        definition = Regex.Replace(rest, @"[\u4e00-\u9fff].*$", "").Trim();
        definition = Regex.Replace(definition, @"•\s*[\u4e00-\u9fff].*$", "").Trim();

        return true;
    }

    public static bool IsExampleLine(string line)
        => !string.IsNullOrWhiteSpace(line) && line.TrimStart().StartsWith("»", StringComparison.Ordinal);

    public static string CleanExampleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        line = line.TrimStart('»', ' ').Trim();

        // Remove Chinese translation at the end
        line = Regex.Replace(line, @"[\u4e00-\u9fff].*$", "");

        // Remove trailing source citations like [OALD]
        line = Regex.Replace(line, @"\s*\[[^\]]+\]$", "");

        // Remove any remaining Chinese characters
        line = Regex.Replace(line, @"[\u4e00-\u9fff]", "");

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

        // Multiple patterns for cross-references
        // Pattern 1: --› see word
        foreach (Match match in CrossReferenceRegex1.Matches(text))
        {
            var word = match.Groups["word"].Value;
            if (!string.IsNullOrWhiteSpace(word))
                crossRefs.Add(word);
        }

        // Pattern 2: variant of word
        foreach (Match match in CrossReferenceRegex2.Matches(text))
        {
            var word = match.Groups["word"].Value;
            if (!string.IsNullOrWhiteSpace(word))
                crossRefs.Add(word);
        }

        // Pattern 3: (also word)
        foreach (Match match in CrossReferenceRegex3.Matches(text))
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

        // Remove parenthetical qualifiers and Chinese text
        pos = Regex.Replace(pos, @"\s*\([^)]*\)", "").Trim();
        pos = Regex.Replace(pos, @"[\u4e00-\u9fff]", "").Trim();
        pos = Regex.Replace(pos, @"^▶\s*", "").Trim(); // Remove ▶ marker

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

    // ★☆☆   East Timor1. ...
    // ★☆☆   East-West▶ adjective
    // Handles optional sense numbers after headword - FIXED to better capture headword
    private static readonly Regex HeadwordRegex =
        new(@"^★+☆+\s+(?<headword>[^\d▶]+?)(?:\s*\d+\.?)?\s*(?:▶\s*)?(?<rest>.*)$",
            RegexOptions.Compiled);

    private static readonly Regex PronunciationRegex =
        new(@"/[^/\n]+/", RegexOptions.Compiled);

    private static readonly Regex SenseNumberRegex =
        new(@"^(?<number>\d+)\.\s*(?<rest>.+)$", RegexOptions.Compiled);

    // Matches parentheses at the beginning of a string
    private static readonly Regex SenseLabelRegex =
        new(@"^\((?<label>[^)]+)\)\s*(?<rest>.+)$", RegexOptions.Compiled);

    // FIXED: Better Chinese translation regex that captures Chinese text after bullet
    private static readonly Regex ChineseTranslationRegex =
        new(@"•\s*(?<translation>[\u4e00-\u9fff].*)$", RegexOptions.Compiled);

    // Matches POS at end after comma
    private static readonly Regex PartOfSpeechRegex =
        new(@",\s*(?<pos>\w+(?:\s+\w+)*?)$", RegexOptions.Compiled);

    // Handles Oxford's block POS markers - FIXED to capture more POS types
    private static readonly Regex BlockPartOfSpeechRegex =
        new(@"▶\s*(?<pos>for abbreviation|adjective|noun|verb|adverb|exclamation|interjection|preposition|conjunction|pronoun|determiner|numeral|prefix|suffix|combining form|idiom|phrasal verb|symbol)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VariantFormsRegex =
        new(@"(?:\(|（)(?:also|也作|亦作|又作)\s*(?<variant>[^)）]+)(?:\)|）)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Multiple cross-reference patterns
    private static readonly Regex CrossReferenceRegex1 =
        new(@"--›\s*(?:see|cf\.?|compare)\s+(?<word>\b[A-Z][A-Za-z\-']+\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CrossReferenceRegex2 =
        new(@"(?:variant of|another term for|同)\s+(?<word>\b[A-Z][A-Za-z\-']+\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CrossReferenceRegex3 =
        new(@"\(also\s+(?<word>\b[A-Z][A-Za-z\-']+\b)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    #endregion Compiled Regex Patterns
}