using System.Text.RegularExpressions;

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
        headword = Regex.Replace(headword, @"\d+\.?$", "").Trim(); // strip trailing sense numbers

        var rest = match.Groups["rest"].Value;

        var pronMatch = PronunciationRegex.Match(rest);
        if (pronMatch.Success)
        {
            pronunciation = pronMatch.Value;
            rest = rest.Replace(pronMatch.Value, "").Trim();
        }

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
            var inlinePosMatch = PartOfSpeechRegex.Match(rest);
            if (inlinePosMatch.Success)
            {
                partOfSpeech = inlinePosMatch.Groups["pos"].Value.Trim();
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

        // (informal), (mass noun), etc.
        var labelMatch = SenseLabelRegex.Match(rest);
        if (labelMatch.Success)
        {
            senseLabel = labelMatch.Groups["label"].Value.Trim();
            rest = labelMatch.Groups["rest"].Value.Trim();
        }

        var translationMatch = ChineseTranslationRegex.Match(rest);
        if (translationMatch.Success)
        {
            chineseTranslation = translationMatch.Groups["translation"].Value.Trim();
        }

        definition = rest;
        return true;
    }

    public static bool IsExampleLine(string line)
        => !string.IsNullOrWhiteSpace(line) && line.StartsWith("»", StringComparison.Ordinal);

    public static string CleanExampleLine(string line)
        => string.IsNullOrWhiteSpace(line) ? string.Empty : line.TrimStart('»', ' ').Trim();

    public static bool IsEntrySeparator(string line)
        => !string.IsNullOrWhiteSpace(line) &&
           line.StartsWith("————————————", StringComparison.Ordinal);

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

        var posMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["noun"] = "noun",
            ["n"] = "noun",
            ["n."] = "noun",
            ["verb"] = "verb",
            ["v"] = "verb",
            ["v."] = "verb",
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
            ["mass noun"] = "noun",
            ["count noun"] = "noun",
            ["prefix"] = "prefix",
            ["suffix"] = "suffix",
            ["numeral"] = "numeral",
            ["num"] = "numeral",
            ["num."] = "numeral"
        };

        return posMap.TryGetValue(pos, out var normalized)
            ? normalized
            : "unk";
    }

    #region Compiled Regex Patterns

    // ★☆☆   East Timor1. ...
    // ★☆☆   East-West▶ adjective
    private static readonly Regex HeadwordRegex =
        new(@"^★+☆+\s+(?<headword>[^\d▶]+)\s*(?:▶\s*)?(?<rest>.*)$",
            RegexOptions.Compiled);

    private static readonly Regex PronunciationRegex =
        new(@"/[^/]+/", RegexOptions.Compiled);

    private static readonly Regex SenseNumberRegex =
        new(@"^(?<number>\d+)\.\s*(?<rest>.+)$", RegexOptions.Compiled);

    private static readonly Regex SenseLabelRegex =
        new(@"^\((?<label>[^)]+)\)\s*(?<rest>.+)$", RegexOptions.Compiled);

    private static readonly Regex ChineseTranslationRegex =
        new(@"•\s*(?<translation>.+)$", RegexOptions.Compiled);

    private static readonly Regex PartOfSpeechRegex =
        new(@",\s*(?<pos>\w+)$", RegexOptions.Compiled);

    private static readonly Regex BlockPartOfSpeechRegex =
        new(@"▶\s*(?<pos>for abbreviation|adjective|noun|verb|adverb|exclamation)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VariantFormsRegex =
        new(@"\((?:也作|亦作)\s*(?<variant>[^)]+)\)",
            RegexOptions.Compiled);

    private static readonly Regex CrossReferenceRegex =
        new(@"\b(?:see|cf\.|compare)\s+(?<word>\b[A-Z][a-z]+\b)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    #endregion Compiled Regex Patterns
}