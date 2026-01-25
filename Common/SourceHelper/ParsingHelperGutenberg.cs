namespace DictionaryImporter.Sources.Common.Helper;

public static class ParsingHelperGutenberg
{
    #region Regex Patterns (Compiled for Performance)

    private const RegexOptions RxC = RegexOptions.Compiled;
    private const RegexOptions RxCI = RegexOptions.Compiled | RegexOptions.IgnoreCase;
    private const RegexOptions RxCM = RegexOptions.Compiled | RegexOptions.Multiline;
    private const RegexOptions RxCIM = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline;
    private const RegexOptions RxCIS = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline;

    // Headword detection
    private static readonly Regex RxAllCapsHeadwordLine =
        new(@"^[A-Z][A-Z0-9\s'\-;,&\.]+[A-Z0-9]$", RxC);

    private static readonly Regex RxHeadwordSimple =
        new(@"^([A-Za-z][A-Za-z'\-]+)(?:\s+\d+)?$", RxC);

    // Sense and definition patterns
    private static readonly Regex RxSenseNumberLine = new(@"^\s*(?<num>\d+)\.\s*(?<content>.+)$", RxCM);
    private static readonly Regex RxLetterSenseLine = new(@"^\s*(?<letter>[a-z])\.\s*(?<content>.+)$", RxCM);
    private static readonly Regex RxNumberOnlyLine = new(@"^\s*\d+\.\s*$", RxCM);

    // Definition markers
    private static readonly Regex RxDefnMarker = new(@"^\s*Defn:\s*(?<content>.+)$", RxCIM);
    private static readonly Regex RxDefinitionMarker = new(@"^\s*Definition:\s*(?<content>.+)$", RxCIM);

    // Etymology patterns
    private static readonly Regex RxEtymologyMarker = new(@"^\s*Etym:\s*(?<content>.+)$", RxCIM);
    private static readonly Regex RxEtymologyBracket = new(@"Etym:\s*\[(?<etym>[^\]]+)\]", RxCI);
    private static readonly Regex RxEtymologyFrom = new(@"^\s*From\s+(?<lang>[A-Z][a-z]+)\s+(?<word>[A-Za-z\-']+)", RxCIM);

    // Synonym patterns
    private static readonly Regex RxSynonymInline =
        new(@"\bSyn\.?\s*(?:--|:)?\s*(?<synonyms>[^\.\n]+)", RxCI);

    private static readonly Regex RxSynonymHeaderOnlyLine =
        new(@"^\s*Syn\.?\s*$", RxCI | RegexOptions.Multiline);

    private static readonly Regex RxSynonymBulletLine =
        new(@"^\s*--\s*(?<content>.+)$", RxCM);

    // FIXED: More stable stop boundary, must stop at:
    // 1) blank line
    // 2) next headword line
    // 3) new marker line Defn:/Etym:
    private static readonly Regex RxSynonymsSection =
        new(@"(?:^|\n)\s*(?:Syn\.|Synonyms?)\s*(?:--|:)?\s*(?<content>.+?)(?=\n\s*\n|\n[A-Z][A-Z0-9\s'\-;,&\.]+\n|\n\s*(?:Defn:|Etym:)\s*|\z)", RxCIS);

    // Part of speech patterns
    private static readonly Regex RxPosAbbreviation =
        new(@"\b(v\.t\.|v\.i\.|v\.|n\.|a\.|adj\.|adv\.|prep\.|conj\.|interj\.|pron\.|art\.|det\.)\b", RxCI);

    private static readonly Regex RxPosFullWord =
        new(@"\b(noun|verb|adjective|adverb|preposition|conjunction|interjection|pronoun|article|determiner)\b", RxCI);

    private static readonly Regex RxPronunciationPosLine =
        new(@"^[A-Za-z][A-Za-z\*""'\-`]+(?:\s+[A-Za-z][A-Za-z\*""'\-`]+)*\s*,\s*(?<pos>v\.t\.|v\.i\.|v\.|n\.|a\.|adj\.|adv\.|prep\.|conj\.|interj\.|pron\.|art\.|det\.)", RxCI);

    private static readonly Regex RxIpaSlashes = new(@"[\/\\]([^\/\\]+)[\/\\]", RxC);

    // Domain/category patterns
    private static readonly Regex RxDomainOnlyLine = new(@"^\((?<domain>[A-Za-z][A-Za-z\- ]*)\.\)$", RxC);
    private static readonly Regex RxDomainInParens = new(@"\((?<domain>[A-Za-z][A-Za-z\-\s]+)\.\)", RxC);

    // Example patterns
    private static readonly Regex RxQuotedExample = new(@"[""']([^""']+?)[""']", RxC);

    // Cross-reference patterns
    private static readonly Regex RxSeeReference =
        new(@"\b[Ss]ee\s+(?:also\s+)?(?<target>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\b", RxC);

    private static readonly Regex RxSameAs =
        new(@"\b[Ss]ame\s+as\s+(?<target>[A-Za-z][a-z]+(?:-[A-Za-z][a-z]+)*)\b", RxC);

    // Cleaning patterns
    private static readonly Regex RxMultipleSpacesSameLine = new(@"[ \t]{2,}", RxC);
    private static readonly Regex RxHyphenatedLineBreak = new(@"(\w+)-\s*\n\s*(\w+)", RxC);
    private static readonly Regex RxLeadingPunctuation = new(@"^[\.\-,;:\s]+", RxC);
    private static readonly Regex RxTrailingPunctuation = new(@"[\.\-,;:\s]+$", RxC);

    // FIXED: punctuation-only should include unicode dashes too
    private static readonly Regex RxPunctuationOnly = new(@"^[^\p{L}\p{N}]+$", RxC);

    private static readonly Regex RxMorphologyBracketBlock = new(@"\[[^\]]{3,400}\]", RxC);

    private static readonly Regex RxMetadataLine =
        new(@"^\s*(?:Pron\.|Note:|Obs\.|Obs:|R\.|Cf\.|Also:)\b.*$", RxCIM);

    // NEW METHOD (added) uses this regex
    private static readonly Regex RxMorphologyInlineNoise =
        new(@"\b(?:imp\.|p\.p\.|p\.pr\.|vb\.n\.|pl\.|sing\.|comp\.|superl\.)\b", RxCI);

    private static readonly string[] NonEnglishPatterns =
    {
        @"[\u4e00-\u9fff]", // Chinese
        @"[\u0400-\u04FF]", // Cyrillic
        @"[\u0600-\u06FF]", // Arabic
        @"[\u0900-\u097F]", // Devanagari
        @"[\u0E00-\u0E7F]", // Thai
        @"[\uAC00-\uD7AF]", // Hangul
        @"[\u3040-\u309F]", // Hiragana
        @"[\u30A0-\u30FF]", // Katakana
    };

    // NEW: boundary fix for Gutenberg merge "meter A merry heart..."
    private static readonly Regex RxLowerToUpperBoundary =
        new(@"(?<=\b[a-z])\s+(?=[A-Z][a-z])", RxC);

    // NEW: header line detector used by transformer
    private static readonly Regex RxHeaderLine =
        new(@"^[A-Za-z""'\-\*`#\s]+\s*,\s*(v\.t\.|v\.i\.|v\.|n\.|a\.|adj\.|adv\.|prep\.)", RxCI);

    #endregion

    #region Constants

    private const int MaxDefinitionLength = 3000;
    private const int MaxRawLineLength = 4000;
    private const int MaxSynonymLength = 80;
    private const int MaxExampleLength = 250;
    private const int MaxFragmentLengthHardCap = 250_000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "but", "in", "on", "at", "to",
        "for", "of", "with", "by", "from", "as", "is", "was", "were",
        "be", "been", "being", "have", "has", "had", "do", "does", "did",
        "etc", "viz"
    };

    private static readonly HashSet<string> KnownDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "Zoöl", "Bot", "Naut", "Med", "Law", "Math", "Chem", "Physiol",
        "Geol", "Gram", "Arch", "Mining", "Mach", "Paint", "Print"
    };

    #endregion

    #region Core Parsing Methods

    public static List<string> SplitIntoEntryBlocks(string rawFragment)
    {
        var blocks = new List<string>();

        if (string.IsNullOrWhiteSpace(rawFragment))
            return blocks;

        var text = NormalizeNewLines(rawFragment);

        if (text.Length > MaxFragmentLengthHardCap)
            text = text[..MaxFragmentLengthHardCap];

        var lines = text.Split('\n');
        var buffer = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i]?.TrimEnd() ?? string.Empty;

            if (line.Length > MaxRawLineLength)
                line = line[..MaxRawLineLength];

            var isPotentialHeadword =
                !string.IsNullOrWhiteSpace(line) &&
                RxAllCapsHeadwordLine.IsMatch(line) &&
                LooksLikeRealHeadwordLine(line);

            var nextLine = i + 1 < lines.Length ? (lines[i + 1] ?? string.Empty).TrimEnd() : string.Empty;

            var isValidHeadwordStart =
                isPotentialHeadword &&
                (string.IsNullOrWhiteSpace(nextLine) ||
                 RxPronunciationPosLine.IsMatch(nextLine) ||
                 nextLine.Contains('"') ||
                 nextLine.Contains('*') ||
                 nextLine.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase) ||
                 nextLine.StartsWith("Defn:", StringComparison.OrdinalIgnoreCase));

            if (isValidHeadwordStart && buffer.Length > 0)
            {
                blocks.Add(buffer.ToString().Trim());
                buffer.Clear();
            }

            if (buffer.Length > 0)
                buffer.Append('\n');

            buffer.Append(line);
        }

        if (buffer.Length > 0)
            blocks.Add(buffer.ToString().Trim());

        return blocks;
    }

    public static string ExtractHeadwordFromBlock(string entryBlock)
    {
        if (string.IsNullOrWhiteSpace(entryBlock))
            return string.Empty;

        var lines = NormalizeNewLines(entryBlock)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return string.Empty;

        var firstLine = lines[0].Trim();

        if (RxAllCapsHeadwordLine.IsMatch(firstLine) && LooksLikeRealHeadwordLine(firstLine))
            return NormalizeHeadword(firstLine);

        var simpleMatch = RxHeadwordSimple.Match(firstLine);
        if (simpleMatch.Success)
            return NormalizeHeadword(simpleMatch.Groups[1].Value);

        var firstWord = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return NormalizeHeadword(firstWord ?? string.Empty);
    }

    public static string ExtractHeadword(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var blocks = SplitIntoEntryBlocks(rawFragment);
        if (blocks.Count > 0)
            return ExtractHeadwordFromBlock(blocks[0]);

        var lines = NormalizeNewLines(rawFragment).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var firstLine = lines.FirstOrDefault()?.Trim() ?? string.Empty;
        return NormalizeHeadword(firstLine);
    }

    public static string ExtractPartOfSpeech(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var text = NormalizeNewLines(rawFragment);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Take(8)
            .ToList();

        foreach (var line in lines)
        {
            var posLineMatch = RxPronunciationPosLine.Match(line);
            if (posLineMatch.Success)
                return NormalizePartOfSpeech(posLineMatch.Groups["pos"].Value);
        }

        var scanText = string.Join("\n", lines);

        var posMatch = RxPosAbbreviation.Match(scanText);
        if (posMatch.Success)
            return NormalizePartOfSpeech(posMatch.Value);

        posMatch = RxPosFullWord.Match(scanText);
        if (posMatch.Success)
            return posMatch.Value.ToLowerInvariant();

        return string.Empty;
    }

    public static List<string> ExtractDefinitions(string rawFragment)
    {
        var definitions = new List<string>();

        if (string.IsNullOrWhiteSpace(rawFragment))
            return definitions;

        var cleanedFragment = CleanRawFragment(rawFragment);
        var blocks = SplitIntoEntryBlocks(cleanedFragment);
        var entryBlock = blocks.Count > 0 ? blocks[0] : cleanedFragment;

        // Remove synonyms & etymology before extracting definitions
        entryBlock = RxSynonymsSection.Replace(entryBlock, "");
        entryBlock = RemoveEtymologyLines(entryBlock);

        // Remove morphology/inflection blocks
        entryBlock = RxMorphologyBracketBlock.Replace(entryBlock, "");

        var lines = NormalizeNewLines(entryBlock)
            .Split('\n')
            .Select(l => (l ?? string.Empty).Trim())
            .ToList();

        var senseLines = new List<(int Sense, string Text)>();

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.Length > MaxRawLineLength)
                line = line[..MaxRawLineLength];

            if (RxAllCapsHeadwordLine.IsMatch(line) && LooksLikeRealHeadwordLine(line))
                continue;

            if (RxPronunciationPosLine.IsMatch(line))
                continue;

            if (RxDomainOnlyLine.IsMatch(line))
                continue;

            if (IsMetadataLine(line))
                continue;

            if (RxNumberOnlyLine.IsMatch(line))
                continue;

            var defnMatch = RxDefnMarker.Match(line);
            if (defnMatch.Success)
            {
                var content = defnMatch.Groups["content"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    senseLines.Add((senseLines.Count + 1, content));
                continue;
            }

            var senseMatch = RxSenseNumberLine.Match(line);
            if (senseMatch.Success)
            {
                var numRaw = senseMatch.Groups["num"].Value;
                if (!int.TryParse(numRaw, out var senseNum))
                    senseNum = senseLines.Count + 1;

                var content = senseMatch.Groups["content"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    senseLines.Add((senseNum, content));
                continue;
            }

            var letterMatch = RxLetterSenseLine.Match(line);
            if (letterMatch.Success)
            {
                var content = letterMatch.Groups["content"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    senseLines.Add((senseLines.Count + 1, content));
                continue;
            }

            if (senseLines.Count > 0)
            {
                if (ShouldAppendContinuationLine(line))
                {
                    var last = senseLines[^1];
                    senseLines[^1] = (last.Sense, $"{last.Text} {line}".Trim());
                }
                continue;
            }

            if (IsCandidateDefinitionLine(line))
            {
                senseLines.Add((1, line));
            }
        }

        foreach (var s in senseLines)
        {
            var def = CleanDefinition(s.Text);
            if (IsGoodDefinition(def))
                definitions.Add(def);
        }

        return definitions
            .Where(IsGoodDefinition)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(d => d.Length > MaxDefinitionLength ? d[..MaxDefinitionLength].TrimEnd() + "..." : d)
            .ToList();
    }

    public static List<string> ExtractSynonyms(string rawFragment)
    {
        var synonyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(rawFragment))
            return new List<string>();

        var text = NormalizeNewLines(rawFragment);
        if (text.Length > MaxFragmentLengthHardCap)
            text = text[..MaxFragmentLengthHardCap];

        var blocks = SplitIntoEntryBlocks(text);
        var entryBlock = blocks.Count > 0 ? blocks[0] : text;

        foreach (Match m in RxSynonymsSection.Matches(entryBlock))
        {
            AddSynonymsFromText(synonyms, m.Groups["content"].Value);
        }

        foreach (Match m in RxSynonymInline.Matches(entryBlock))
        {
            AddSynonymsFromText(synonyms, m.Groups["synonyms"].Value);
        }

        foreach (Match m in RxSameAs.Matches(entryBlock))
        {
            var syn = CleanSynonym(m.Groups["target"].Value);
            if (IsGoodSynonym(syn))
                synonyms.Add(syn);
        }

        if (RxSynonymHeaderOnlyLine.IsMatch(entryBlock))
        {
            foreach (Match m in RxSynonymBulletLine.Matches(entryBlock))
            {
                AddSynonymsFromText(synonyms, m.Groups["content"].Value);
            }
        }

        return synonyms
            .Where(IsGoodSynonym)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ExtractEtymology(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var text = NormalizeNewLines(rawFragment);
        if (text.Length > MaxFragmentLengthHardCap)
            text = text[..MaxFragmentLengthHardCap];

        var blocks = SplitIntoEntryBlocks(text);
        var entryBlock = blocks.Count > 0 ? blocks[0] : text;

        var etymologies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in RxEtymologyBracket.Matches(entryBlock))
        {
            var ety = CleanEtymology(match.Groups["etym"].Value);
            if (!string.IsNullOrWhiteSpace(ety))
                etymologies.Add(ety);
        }

        foreach (Match match in RxEtymologyMarker.Matches(entryBlock))
        {
            var ety = CleanEtymology(match.Groups["content"].Value);
            if (!string.IsNullOrWhiteSpace(ety))
                etymologies.Add(ety);
        }

        foreach (Match match in RxEtymologyFrom.Matches(entryBlock))
        {
            var ety = $"{match.Groups["lang"].Value} {match.Groups["word"].Value}";
            ety = CleanEtymology(ety);
            if (!string.IsNullOrWhiteSpace(ety))
                etymologies.Add(ety);
        }

        return string.Join("; ", etymologies);
    }

    public static string ExtractPronunciation(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var text = NormalizeNewLines(rawFragment);
        if (text.Length > MaxFragmentLengthHardCap)
            text = text[..MaxFragmentLengthHardCap];

        var ipaMatch = RxIpaSlashes.Match(text);
        if (ipaMatch.Success)
            return ipaMatch.Groups[1].Value.Trim();

        var blocks = SplitIntoEntryBlocks(text);
        var entryBlock = blocks.Count > 0 ? blocks[0] : text;

        var lines = entryBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).ToList();

        if (lines.Count >= 2)
        {
            var line = lines[1];
            if (line.Contains('"') || line.Contains('*') || line.Contains('`'))
            {
                var idx = line.IndexOf(',');
                if (idx > 0)
                    return line[..idx].Trim();
            }
        }

        return string.Empty;
    }

    public static List<string> ExtractExamples(string rawFragment)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(rawFragment))
            return examples;

        var text = NormalizeNewLines(rawFragment);
        if (text.Length > MaxFragmentLengthHardCap)
            text = text[..MaxFragmentLengthHardCap];

        var blocks = SplitIntoEntryBlocks(text);
        var entryBlock = blocks.Count > 0 ? blocks[0] : text;

        entryBlock = RxSynonymsSection.Replace(entryBlock, "");

        foreach (Match match in RxQuotedExample.Matches(entryBlock))
        {
            var example = CleanExample(match.Groups[1].Value);

            if (string.IsNullOrWhiteSpace(example)) continue;
            if (example.Length < 6 || example.Length > MaxExampleLength) continue;

            if (example.Contains("Defn", StringComparison.OrdinalIgnoreCase) ||
                example.Contains("Etym", StringComparison.OrdinalIgnoreCase) ||
                example.Contains("Syn.", StringComparison.OrdinalIgnoreCase))
                continue;

            examples.Add(example);
        }

        return examples.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> ExtractDomains(string rawFragment)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(rawFragment))
            return new List<string>();

        var text = NormalizeNewLines(rawFragment);
        if (text.Length > MaxFragmentLengthHardCap)
            text = text[..MaxFragmentLengthHardCap];

        var blocks = SplitIntoEntryBlocks(text);
        var entryBlock = blocks.Count > 0 ? blocks[0] : text;

        foreach (Match match in RxDomainInParens.Matches(entryBlock))
        {
            var d = NormalizeDomain(match.Groups["domain"].Value);
            if (!string.IsNullOrWhiteSpace(d))
                domains.Add(d);
        }

        return domains.Select(d => d.ToLowerInvariant()).ToList();
    }

    #endregion

    #region Gutenberg Transformer Helper Methods (NEW)

    // ✅ Used ONLY by GutenbergWebsterTransformer
    public static IEnumerable<string> ExtractDefinitionsFromRawLines(List<string> lines)
    {
        if (lines == null || lines.Count == 0)
            yield break;

        var buffer = new List<string>();
        var started = false;

        foreach (var rawLine in lines)
        {
            var line = (rawLine ?? string.Empty).Trim();
            if (line.Length == 0)
                continue;

            if (!started && LooksLikeGutenbergHeaderLine(line))
                continue;

            // ✅ SKIP Etym: completely (never becomes a meaning definition)
            if (line.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase))
            {
                if (buffer.Count > 0)
                {
                    yield return string.Join(" ", buffer).Trim();
                    buffer.Clear();
                }

                started = true;
                continue;
            }

            // ✅ Defn: starts a new definition
            if (line.StartsWith("Defn:", StringComparison.OrdinalIgnoreCase))
            {
                started = true;

                if (buffer.Count > 0)
                {
                    yield return string.Join(" ", buffer).Trim();
                    buffer.Clear();
                }

                buffer.Add(line[5..].Trim());
                continue;
            }

            // ✅ Numbered definition starts a new definition
            if (Regex.IsMatch(line, @"^\d+\.\s+"))
            {
                started = true;

                if (buffer.Count > 0)
                {
                    yield return string.Join(" ", buffer).Trim();
                    buffer.Clear();
                }

                buffer.Add(Regex.Replace(line, @"^\d+\.\s+", "").Trim());
                continue;
            }

            if (!started)
                continue;

            if (IsMetadataLine(line))
                continue;

            buffer.Add(line);
        }

        if (buffer.Count > 0)
            yield return string.Join(" ", buffer).Trim();
    }

    public static bool LooksLikeGutenbergHeaderLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var t = line.Trim();

        if (RxHeaderLine.IsMatch(t))
            return true;

        // "A (# emph. #)." style
        if (Regex.IsMatch(t, @"^\w+\s*\(#", RegexOptions.IgnoreCase | RegexOptions.Compiled))
            return true;

        return false;
    }

    public static string NormalizeGutenbergTransformerDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var cleaned = definition.Trim();

        cleaned = RxDefnMarker.Replace(cleaned, "${content}");
        cleaned = RxDefinitionMarker.Replace(cleaned, "${content}");

        cleaned = RxMultipleSpacesSameLine.Replace(cleaned, " ").Trim();

        // Fix: missing boundary between definition and example/citation
        cleaned = FixMissingDefinitionExampleBoundary(cleaned);

        if (!string.IsNullOrWhiteSpace(cleaned) &&
            !cleaned.EndsWith(".") &&
            !cleaned.EndsWith("!") &&
            !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned.Trim();
    }

    public static string FixMissingDefinitionExampleBoundary(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        // Conservative fix: inserts ". " for lower->Upper boundary
        // Example: "meter A merry" => "meter. A merry"
        var d = RxLowerToUpperBoundary.Replace(definition.Trim(), ". ");
        d = d.Replace("..", ".");
        return d.Trim();
    }

    #endregion

    #region Cleaning Methods

    public static string CleanRawFragment(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return string.Empty;

        var cleaned = NormalizeNewLines(rawFragment);
        if (cleaned.Length > MaxFragmentLengthHardCap)
            cleaned = cleaned[..MaxFragmentLengthHardCap];

        cleaned = RxHyphenatedLineBreak.Replace(cleaned, "$1$2");

        var fixedLines = cleaned.Split('\n').Select(l =>
        {
            var line = l ?? string.Empty;
            if (line.Length > MaxRawLineLength) line = line[..MaxRawLineLength];
            return RxMultipleSpacesSameLine.Replace(line, " ").TrimEnd();
        }).ToArray();

        cleaned = string.Join("\n", fixedLines);

        cleaned = cleaned.Replace("~~~~", "")
            .Replace("****", "")
            .Replace("\t", " ")
            .Replace("....", ".")
            .Replace("..", ".")
            .Trim();

        return cleaned;
    }

    private static string CleanDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return string.Empty;

        var cleaned = definition.Trim();

        cleaned = RxDefnMarker.Replace(cleaned, "${content}");
        cleaned = RxDefinitionMarker.Replace(cleaned, "${content}");

        cleaned = RxMorphologyBracketBlock.Replace(cleaned, "");
        cleaned = RxLeadingPunctuation.Replace(cleaned, "");
        cleaned = RxTrailingPunctuation.Replace(cleaned, "");

        cleaned = RemoveInlineMorphologyNoise(cleaned);

        cleaned = RxMultipleSpacesSameLine.Replace(cleaned, " ").Trim();

        cleaned = FixMissingDefinitionExampleBoundary(cleaned);

        if (!string.IsNullOrWhiteSpace(cleaned) &&
            !cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned.Trim();
    }

    private static void AddSynonymsFromText(HashSet<string> synonyms, string synonymText)
    {
        if (string.IsNullOrWhiteSpace(synonymText))
            return;

        var raw = synonymText.Replace("\r", "")
            .Replace("\n", " ")
            .Replace("--", " ")
            .Trim();

        var stopMarkers = new[] { "Defn:", "Etym:", "Note:" };
        foreach (var marker in stopMarkers)
        {
            var idx = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                raw = raw[..idx].Trim();
        }

        var parts = Regex.Split(raw, @"[,;:/\|]|\s+\band\b\s+", RegexOptions.IgnoreCase);

        foreach (var p in parts)
        {
            var token = (p ?? string.Empty).Trim();
            if (token.Length == 0)
                continue;

            if (token.StartsWith("See", StringComparison.OrdinalIgnoreCase))
                continue;

            token = token.Replace("Syn.", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Synonyms", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            var dotIdx = token.IndexOf('.');
            if (dotIdx > 0 && dotIdx < token.Length - 1)
                token = token[..dotIdx];

            var cleanedSyn = CleanSynonym(token);
            if (IsGoodSynonym(cleanedSyn))
                synonyms.Add(cleanedSyn);
        }
    }

    private static string CleanSynonym(string synonym)
    {
        if (string.IsNullOrWhiteSpace(synonym))
            return string.Empty;

        var cleaned = synonym.Trim();

        cleaned = Regex.Replace(cleaned,
            @"\b(Shak|Chaucer|Milton|Dryden|Lowell|Tennyson|Wordsworth|Bible)\b\.?",
            "",
            RxCI).Trim();

        cleaned = Regex.Replace(cleaned, @"\([^)]*\)", "").Trim();
        cleaned = Regex.Replace(cleaned, @"\b(?:etc|&c)\b\.?", "", RxCI).Trim();

        cleaned = cleaned.Trim('"', '\'', '`', ' ', '.', ',', ';', ':', '-', '—', '(', ')');
        cleaned = RxMultipleSpacesSameLine.Replace(cleaned, " ").Trim();

        if (cleaned.Length == 0)
            return string.Empty;

        if (cleaned.Length > MaxSynonymLength)
            cleaned = cleaned[..MaxSynonymLength].Trim();

        var wordCount = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 4)
            return string.Empty;

        if (IsPunctuationOnly(cleaned))
            return string.Empty;

        if (!cleaned.Any(char.IsLetter))
            return string.Empty;

        return ToTitleCasePreserveMultiWord(cleaned);
    }

    private static string CleanEtymology(string etymology)
    {
        if (string.IsNullOrWhiteSpace(etymology))
            return string.Empty;

        var cleaned = etymology.Trim().TrimEnd('.', ',', ';', ':');
        cleaned = cleaned.Replace("[", "").Replace("]", "").Trim();
        cleaned = RxMultipleSpacesSameLine.Replace(cleaned, " ").Trim();
        return cleaned;
    }

    private static string CleanExample(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        var cleaned = example.Trim().Trim('"', '\'', '`').Trim();
        cleaned = RxMultipleSpacesSameLine.Replace(cleaned, " ").Trim();

        if (cleaned.Length > 0 && char.IsLower(cleaned[0]))
            cleaned = char.ToUpperInvariant(cleaned[0]) + cleaned[1..];

        if (!string.IsNullOrWhiteSpace(cleaned) &&
            !cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned;
    }

    private static bool IsPunctuationOnly(string text)
        => string.IsNullOrWhiteSpace(text) || RxPunctuationOnly.IsMatch(text.Trim());

    public static string NormalizeWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return string.Empty;

        return word.Trim()
            .ToLowerInvariant()
            .Replace("-", "")
            .Replace("'", "")
            .Replace(" ", "");
    }

    public static string NormalizeHeadword(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = text.Trim().Replace("  ", " ").Trim();
        cleaned = cleaned.Trim('"', '\'', '.');

        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(ToTitleCase));
    }

    private static string NormalizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return string.Empty;

        var cleaned = domain.Trim().TrimEnd('.').Replace(" ", "");

        if (KnownDomains.Contains(cleaned))
            return cleaned;

        return domain.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string NormalizePartOfSpeech(string pos)
    {
        if (string.IsNullOrWhiteSpace(pos))
            return string.Empty;

        var p = pos.Trim().TrimEnd('.').ToLowerInvariant();
        return p switch
        {
            "n" => "noun",
            "v" or "v.t" or "v.i" => "verb",
            "a" or "adj" => "adjective",
            "adv" => "adverb",
            "prep" => "preposition",
            "conj" => "conjunction",
            "interj" => "interjection",
            "pron" => "pronoun",
            "art" => "article",
            "det" => "determiner",
            _ => p
        };
    }

    #endregion

    #region Fallback Methods

    public static ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
    {
        if (entry == null)
            return new ParsedDefinition();

        var headword = entry.Word ?? "Unknown";
        var pos = "noun";

        try
        {
            var cleanedDefinition = CleanRawFragment(entry.Definition ?? string.Empty);
            var blocks = SplitIntoEntryBlocks(cleanedDefinition);
            var entryBlock = blocks.Count > 0 ? blocks[0] : cleanedDefinition;

            var definitions = ExtractDefinitions(entryBlock);
            var firstDefinition = definitions.FirstOrDefault() ?? string.Empty;

            var extractedPos = ExtractPartOfSpeech(entryBlock);
            if (!string.IsNullOrWhiteSpace(extractedPos))
                pos = extractedPos;

            var extractedHeadword = ExtractHeadwordFromBlock(entryBlock);
            if (!string.IsNullOrWhiteSpace(extractedHeadword) && !IsPunctuationOnly(extractedHeadword))
                headword = extractedHeadword;
            else if (string.IsNullOrWhiteSpace(headword) || IsPunctuationOnly(headword))
                headword = "Unknown";

            return new ParsedDefinition
            {
                MeaningTitle = string.Empty,
                Definition = firstDefinition,
                RawFragment = entry.Definition ?? string.Empty,
                SenseNumber = entry.SenseNumber,
                PartOfSpeech = pos,
                Domain = ExtractDomains(entryBlock).FirstOrDefault(),
                Synonyms = ExtractSynonyms(entryBlock),
                CrossReferences = new List<CrossReference>(),
                Etymology = ExtractEtymology(entryBlock),
                Examples = ExtractExamples(entryBlock),
                DedupKey = GenerateDedupKey(headword, pos)
            };
        }
        catch
        {
            return new ParsedDefinition
            {
                MeaningTitle = string.Empty,
                Definition = string.Empty,
                RawFragment = entry.Definition ?? string.Empty,
                SenseNumber = entry.SenseNumber,
                PartOfSpeech = pos,
                Synonyms = new List<string>(),
                Etymology = string.Empty,
                Examples = new List<string>(),
                CrossReferences = new List<CrossReference>(),
                DedupKey = GenerateDedupKey(headword, pos)
            };
        }
    }

    public static string ExtractFallbackPartOfSpeech(DictionaryEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Definition))
            return "noun";

        var definition = entry.Definition.ToLowerInvariant();

        if (definition.Contains(" v.t.") || definition.Contains(" v.i.") || definition.Contains(" verb")) return "verb";
        if (definition.Contains(" n.") || definition.Contains(" noun")) return "noun";
        if (definition.Contains(" a.") || definition.Contains(" adj.")) return "adjective";
        if (definition.Contains(" adv.")) return "adverb";

        return "noun";
    }

    #endregion

    #region Helper Methods for Parser

    public static bool IsValidFragment(string rawFragment)
    {
        if (string.IsNullOrWhiteSpace(rawFragment))
            return false;

        var text = NormalizeNewLines(rawFragment);
        if (text.Length > MaxFragmentLengthHardCap)
            text = text[..MaxFragmentLengthHardCap];

        var hasLetters = text.Any(char.IsLetter);
        var hasReasonableLength = text.Length > 20 && text.Length < 200_000;

        return hasLetters && hasReasonableLength;
    }

    public static bool ContainsNonEnglishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var pattern in NonEnglishPatterns)
        {
            if (Regex.IsMatch(text, pattern))
                return true;
        }

        return false;
    }

    public static bool IsMetadataLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;

        var t = line.Trim();

        if (RxMetadataLine.IsMatch(t))
            return true;

        if (t.StartsWith("See ", StringComparison.OrdinalIgnoreCase))
            return true;

        if (t.StartsWith("Comp.", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Superl.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (LooksLikeMostlyMorphology(t))
            return true;

        return false;
    }

    public static string GenerateDedupKey(string word, string partOfSpeech, string source = "GUTENBERG")
    {
        if (string.IsNullOrWhiteSpace(word))
            return string.Empty;

        var normalizedWord = NormalizeWordForDedup(word);
        var normalizedPos = NormalizePartOfSpeechForDedup(partOfSpeech);
        var normalizedSource = (source ?? "GUTENBERG").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedPos))
            normalizedPos = "UNKNOWN";

        return $"{normalizedSource}_{normalizedWord}_{normalizedPos}";
    }

    public static string NormalizeWordForDedup(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return string.Empty;

        if (!word.Any(char.IsLetter))
            return "UNKNOWN";

        var w = word.Trim().ToUpperInvariant();
        w = Regex.Replace(w, @"\s+", "_");
        w = w.Replace("'", "")
            .Replace("\"", "")
            .Replace(".", "")
            .Replace("&", "AND")
            .Replace("-", "_")
            .Replace("—", "_")
            .Replace("–", "_");

        w = Regex.Replace(w, @"_+", "_").Trim('_');

        if (w.Length > 120)
            w = w[..120];

        return w;
    }

    public static string NormalizePartOfSpeechForDedup(string pos)
    {
        if (string.IsNullOrWhiteSpace(pos))
            return "UNKNOWN";

        var normalized = pos.Trim().ToUpperInvariant().TrimEnd('.');

        return normalized switch
        {
            "V.T" or "V.I" or "V" or "VB" or "VERB" => "VERB",
            "N" or "NOUN" => "NOUN",
            "A" or "ADJ" or "ADJECTIVE" => "ADJECTIVE",
            "ADV" or "ADVERB" => "ADVERB",
            _ => normalized.Length > 25 ? "UNKNOWN" : normalized
        };
    }

    public static bool ShouldProcessWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        var trimmed = word.Trim();

        if (trimmed.Length == 1)
            return new HashSet<string> { "A", "I", "O", "a", "i", "o" }.Contains(trimmed);

        if (trimmed.Length > 80)
            return false;

        if (!trimmed.Any(char.IsLetter))
            return false;

        if (StopWords.Contains(trimmed.ToLowerInvariant()) && trimmed.Length < 3)
            return false;

        return true;
    }

    #endregion

    #region Internal Helpers

    private static string NormalizeNewLines(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string RemoveEtymologyLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = NormalizeNewLines(text).Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = (line ?? string.Empty).Trim();
            if (trimmed.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (sb.Length > 0)
                sb.Append('\n');

            sb.Append(line);
        }

        return sb.ToString();
    }

    private static bool IsGoodDefinition(string def)
    {
        if (string.IsNullOrWhiteSpace(def))
            return false;

        var d = def.Trim();

        if (d.Length < 10)
            return false;

        if (!d.Any(char.IsLetter))
            return false;

        if (RxDomainOnlyLine.IsMatch(d))
            return false;

        if (Regex.IsMatch(d, @"^\d+\.?$"))
            return false;

        if (d.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("Synonyms", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (d.StartsWith("See ", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("Comp.", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("Superl.", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsCandidateDefinitionLine(string line)
    {
        var t = (line ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(t))
            return false;

        if (t.Length < 6)
            return false;

        if (RxAllCapsHeadwordLine.IsMatch(t) && LooksLikeRealHeadwordLine(t))
            return false;

        if (RxPronunciationPosLine.IsMatch(t))
            return false;

        if (IsMetadataLine(t))
            return false;

        if (RxDomainOnlyLine.IsMatch(t))
            return false;

        if (RxNumberOnlyLine.IsMatch(t))
            return false;

        if (t.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Synonyms", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!t.Any(char.IsLetter))
            return false;

        return true;
    }

    private static bool IsGoodSynonym(string syn)
    {
        if (string.IsNullOrWhiteSpace(syn))
            return false;

        var s = syn.Trim();

        if (s.Length < 2 || s.Length > MaxSynonymLength)
            return false;

        if (IsPunctuationOnly(s))
            return false;

        if (s.Equals("syn", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("synonyms", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("etc", StringComparison.OrdinalIgnoreCase))
            return false;

        if (StopWords.Contains(s.ToLowerInvariant()))
            return false;

        if (!s.Any(char.IsLetter))
            return false;

        if (s.Length == 1 &&
            !string.Equals(s, "A", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(s, "I", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool LooksLikeRealHeadwordLine(string allCapsLine)
    {
        if (string.IsNullOrWhiteSpace(allCapsLine))
            return false;

        var line = allCapsLine.Trim();

        if (line.Length < 2 || line.Length > 80)
            return false;

        if (!line.Any(char.IsLetter))
            return false;

        if (line.Contains("CHAPTER", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("APPENDIX", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool ShouldAppendContinuationLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var t = line.Trim();

        if (RxAllCapsHeadwordLine.IsMatch(t) && LooksLikeRealHeadwordLine(t))
            return false;

        if (RxSenseNumberLine.IsMatch(t) || RxLetterSenseLine.IsMatch(t))
            return false;

        if (RxNumberOnlyLine.IsMatch(t))
            return false;

        if (t.StartsWith("Syn.", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Synonyms", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Etym:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Defn:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (RxDomainOnlyLine.IsMatch(t))
            return false;

        if (IsMetadataLine(t))
            return false;

        return true;
    }

    private static string ToTitleCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Trim();

        if (t.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts.Select(p =>
            {
                var lower = p.ToLowerInvariant();
                return lower.Length > 0 ? char.ToUpperInvariant(lower[0]) + lower[1..] : lower;
            }));
        }

        if (t.Length == 1)
            return t.ToUpperInvariant();

        return char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant();
    }

    private static string ToTitleCasePreserveMultiWord(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return ToTitleCase(text);

        return string.Join(" ", parts.Select(ToTitleCase));
    }

    private static bool LooksLikeMostlyMorphology(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var t = text.Trim();

        if (RxMorphologyInlineNoise.IsMatch(t))
        {
            var letters = t.Count(char.IsLetter);
            var spaces = t.Count(char.IsWhiteSpace);
            var total = t.Length;

            if (total > 0 && letters < 12 && spaces > 2)
                return true;
        }

        return false;
    }

    private static string RemoveInlineMorphologyNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text;

        t = Regex.Replace(t, @"\b(?:imp\.|p\.p\.|p\.pr\.|vb\.n\.|pl\.|sing\.)\b", "", RxCI);

        t = RxMultipleSpacesSameLine.Replace(t, " ").Trim();
        return t;
    }
    public static bool ValidateSynonymPair(string headwordA, string headwordB)
    {
        if (string.IsNullOrWhiteSpace(headwordA) || string.IsNullOrWhiteSpace(headwordB))
            return false;

        var a = NormalizeWordForDedup(headwordA);
        var b = NormalizeWordForDedup(headwordB);

        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        // Same word -> not a synonym
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return false;

        // Reject too short junk
        if (a.Length < 2 || b.Length < 2)
            return false;

        // Reject punctuation-only cases
        if (!a.Any(char.IsLetter) || !b.Any(char.IsLetter))
            return false;

        return true;
    }

    #endregion

    #region Progress Tracking and Logging

    public static void LogExtractionStart(ILogger logger, string sourceName)
    {
        logger.LogInformation("{Source} extraction started", sourceName);
    }

    public static void LogExtractionComplete(ILogger logger, string sourceName, long entryCount)
    {
        logger.LogInformation("{Source} extraction completed. Entries: {Count}",
            sourceName, entryCount);
    }

    public static void LogExtractionProgress(ILogger logger, string sourceName, long entryCount, int interval = 1000)
    {
        if (entryCount % interval == 0)
        {
            logger.LogInformation("{Source} extraction progress: {Count} entries processed",
                sourceName, entryCount);
        }
    }

    public static ExtractorContext CreateExtractorContext(ILogger logger, string sourceName)
    {
        return new ExtractorContext
        {
            Logger = logger,
            SourceName = sourceName,
            EntryCount = 0
        };
    }

    public static void UpdateProgress(ref ExtractorContext context)
    {
        context.EntryCount++;
        LogExtractionProgress(context.Logger, context.SourceName, context.EntryCount);
    }

    #endregion Progress Tracking and Logging

    #region Stream Processing

    public static async IAsyncEnumerable<string> ProcessGutenbergStreamAsync(
        Stream stream,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 16 * 1024, true);

        string? line;
        var bodyStarted = false;
        long lineCount = 0;
        long bodyLineCount = 0;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineCount++;

            if (!bodyStarted)
            {
                if (line.StartsWith("*** START"))
                {
                    bodyStarted = true;
                    logger.LogInformation("Gutenberg body detected at line {LineNumber}", lineCount);
                }

                continue;
            }

            if (line.StartsWith("*** END"))
            {
                logger.LogInformation("Gutenberg end marker at line {LineNumber}", lineCount);
                break;
            }

            bodyLineCount++;
            yield return line;
        }

        logger.LogInformation(
            "Gutenberg stream processing completed: {BodyLines} body lines (TotalLinesRead={TotalLines})",
            bodyLineCount,
            lineCount);
    }

    #endregion Stream Processing

    #region Helper Classes

    public class ExtractorContext
    {
        public ILogger Logger { get; set; } = null!;
        public string SourceName { get; set; } = null!;
        public long EntryCount { get; set; }
    }

    #endregion Helper Classes

    private static readonly char[] _quoteChars = { '"', '\'', '`', '«', '»', '「', '」', '『', '』' };
    private static readonly string[] _templateMarkers = { "{{", "}}", "[[", "]]" };

    private static readonly Dictionary<string, string> _languagePatterns = new()
    {
        { @"\bLatin\b", "la" },
        { @"\bAncient Greek\b|\bGreek\b", "el" },
        { @"\bFrench\b", "fr" },
        { @"\bGerman(ic)?\b", "de" },
        { @"\bOld English\b", "ang" },
        { @"\bMiddle English\b", "enm" },
        { @"\bItalian\b", "it" },
        { @"\bSpanish\b", "es" },
        { @"\bDutch\b", "nl" },
        { @"\bProto-Indo-European\b", "ine-pro" },
        { @"\bOld Norse\b", "non" },
        { @"\bOld French\b", "fro" },
        { @"\bAnglo-Norman\b", "xno" }
    };

    /// <summary>
    /// Cleans etymology text by removing template markers and HTML.
    /// </summary>
    public static string CleanEtymologyText(string etymology)
    {
        if (string.IsNullOrWhiteSpace(etymology))
            return string.Empty;

        var cleaned = Regex.Replace(etymology, @"\s+", " ").Trim();

        foreach (var marker in _templateMarkers)
        {
            cleaned = cleaned.Replace(marker, "");
        }

        cleaned = Regex.Replace(cleaned, @"<[^>]+>", "");

        return cleaned.Trim();
    }

    /// <summary>
    /// Cleans example text by removing quotes and translations.
    /// </summary>
    public static string CleanExampleText(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        var cleaned = example.Trim(_quoteChars);
        cleaned = Regex.Replace(cleaned, @"\s*\([^)]*\)\s*", " ");

        if (!cleaned.EndsWith(".") && !cleaned.EndsWith("!") && !cleaned.EndsWith("?"))
        {
            cleaned += ".";
        }

        return cleaned.Trim();
    }

    /// <summary>
    /// Detects language code from etymology text.
    /// </summary>
    public static string? DetectLanguageFromEtymology(string etymology)
    {
        if (string.IsNullOrWhiteSpace(etymology))
            return null;

        foreach (var pattern in _languagePatterns)
        {
            if (Regex.IsMatch(etymology, pattern.Key, RegexOptions.IgnoreCase))
            {
                return pattern.Value;
            }
        }

        return null;
    }
    public static bool IsGutenbergHeadwordLine(string? line, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var t = line.Trim();

        if (t.Length < 2 || t.Length > maxLength)
            return false;

        // Must match Gutenberg/Webster headword format
        if (!RxAllCapsHeadwordLine.IsMatch(t))
            return false;

        return LooksLikeRealHeadwordLine(t);
    }

}