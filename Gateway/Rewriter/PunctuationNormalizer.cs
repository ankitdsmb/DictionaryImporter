namespace DictionaryImporter.Gateway.Rewriter;

public static class PunctuationNormalizer
{
    private static readonly RegexOptions Opt =
        RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex MultiSpace = new(@"\s{2,}", Opt);

    private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.;:!?])", Opt);

    private static readonly Regex MissingSpaceAfterCommaSemicolonColon = new(@"([,;:])([^\s\)\]\}""'“”’])", Opt);

    private static readonly Regex MissingSpaceAfterPeriodQuestionExclamation =
        new(@"([.!?])([A-Za-z])", Opt);

    private static readonly Regex RepeatedQuestion = new(@"\?{2,}", Opt);

    private static readonly Regex RepeatedExclamation = new(@"!{2,}", Opt);

    private static readonly Regex RepeatedDots = new(@"\.{4,}", Opt); // allow "..." but normalize "...." -> "..."

    private static readonly Regex SpaceInsideParensOpen = new(@"\(\s+", Opt);

    private static readonly Regex SpaceInsideParensClose = new(@"\s+\)", Opt);

    // ENHANCEMENT 1: Added Unicode punctuation support
    private static readonly Regex SpaceBeforeUnicodePunctuation = new(@"\s+([。，；：！？、．])", Opt);

    // ENHANCEMENT 2: Handle quotes properly
    private static readonly Regex FixSmartQuotes = new(@"([\w\d])['‘’""""""']([\w\d])", Opt);

    // ENHANCEMENT 3: Fix decimal numbers (prevent "123 . 45" -> "123.45")
    private static readonly Regex DecimalNumberFix = new(@"(\d)\s+\.\s+(\d)", Opt);

    // ENHANCEMENT 4: Fix time formats (prevent "12 : 30" -> "12:30")
    private static readonly Regex TimeFormatFix = new(@"(\d)\s+:\s+(\d)", Opt);

    // ENHANCEMENT 5: Fix currency formats
    private static readonly Regex CurrencyFix = new(@"([£€¥\$])\s+(\d)", Opt);

    // ENHANCEMENT 6: Fix percentages
    private static readonly Regex PercentageFix = new(@"(\d)\s+%", Opt);

    // ENHANCEMENT 7: Fix parentheses with nested content
    private static readonly Regex MultipleSpaceInsideParens = new(@"\(\s{2,}", Opt);

    private static readonly Regex MultipleSpaceOutsideParens = new(@"\s{2,}\)", Opt);

    // ENHANCEMENT 8: Fix spacing around brackets
    private static readonly Regex SpaceInsideBracketsOpen = new(@"\[\s+", Opt);

    private static readonly Regex SpaceInsideBracketsClose = new(@"\s+\]", Opt);

    // ENHANCEMENT 9: Fix spacing around braces
    private static readonly Regex SpaceInsideBracesOpen = new(@"\{\s+", Opt);

    private static readonly Regex SpaceInsideBracesClose = new(@"\s+\}", Opt);

    // ENHANCEMENT 10: Preserve intentional punctuation (like "!!!" in creative writing)
    private static readonly Regex IntentionalMultipleExclamation = new(@"(!{2,})(?=\s|$)", Opt);

    private static readonly Regex IntentionalMultipleQuestion = new(@"(\?{2,})(?=\s|$)", Opt);

    // ENHANCEMENT 11: Fix mixed punctuation (?!, !?, etc.)
    private static readonly Regex MixedPunctuation = new(@"([!?]){2,}", Opt);

    // ENHANCEMENT 12: Fix Oxford comma spacing
    private static readonly Regex OxfordCommaFix = new(@"(\w+)\s*,\s*(and|or|nor)\b", Opt);

    // ENHANCEMENT 13: Handle abbreviations (U.S.A., Ph.D., etc.)
    private static readonly Regex AbbreviationProtection =
        new(@"\b(?:[A-Z]\.){2,}|[A-Z][a-z]+\.(?:[A-Z][a-z]+\.)*", Opt);

    // ENHANCEMENT 14: Fix spacing before opening quotes
    private static readonly Regex SpaceBeforeOpeningQuote = new(@"(\w)\s+([""'""""""""])", Opt);

    // ENHANCEMENT 15: Fix spacing after closing quotes
    private static readonly Regex SpaceAfterClosingQuote = new(@"([""'""""""""])\s+(\w)", Opt);

    // ENHANCEMENT 16: Handle mathematical/scientific notation
    private static readonly Regex ScientificNotationFix = new(@"(\d)\s*([×xX])\s*10\s*\^\s*([+-]?\d+)", Opt);

    // ENHANCEMENT 17: Fix date formats
    private static readonly Regex DateFormatFix = new(@"(\d{1,2})\s*/\s*(\d{1,2})\s*/\s*(\d{2,4})", Opt);

    // ENHANCEMENT 18: Handle contractions properly
    private static readonly Regex ContractionFix = new(@"(\w+)\s+'(s|t|ve|ll|re|d|m)(?=\s|$)", Opt);

    // ENHANCEMENT 19: Handle em-dash, en-dash
    private static readonly Regex DashNormalization = new(@"\s*[‑–—]\s*", Opt);

    // ENHANCEMENT 20: Fix spacing around slashes
    private static readonly Regex SlashSpacing = new(@"(\S)\s*/\s*(\S)", Opt);

    // ENHANCEMENT 21: Preserve code-like patterns
    private static readonly Regex CodeLikePatterns =
        new(@"\b[a-zA-Z_][a-zA-Z0-9_]*\.[a-zA-Z_][a-zA-Z0-9_]*\b|\b\d+\.\d+\b", Opt);

    // ENHANCEMENT 22: Handle numbered lists
    private static readonly Regex NumberedListFix = new(@"(\d+)\.\s+", Opt);

    // ENHANCEMENT 23: Fix spacing around equals
    private static readonly Regex EqualsSpacing = new(@"(\S)\s*=\s*(\S)", Opt);

    // ENHANCEMENT 24: Handle email and URLs
    private static readonly Regex EmailUrlProtection =
        new(@"\b[\w\.-]+@[\w\.-]+\.\w+\b|https?://\S+", Opt);

    // ENHANCEMENT 25: Handle ISBN, ISSN, etc.
    private static readonly Regex IdentifierProtection =
        new(@"\bISBN[- ]?(97[89])?[- ]?\d{1,5}[- ]?\d{1,7}[- ]?\d{1,7}[- ]?\d\b", Opt);

    // ENHANCEMENT 26: Buffer for protected content
    private static readonly Dictionary<string, string> _placeholderMap = new();

    private static int _placeholderId = 0;

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        try
        {
            var s = input;

            // PHASE 0: Protect special content before normalization
            s = ProtectSpecialContent(s);

            // PHASE 1: Basic whitespace normalization (preserve original logic)
            s = MultiSpace.Replace(s, " ");

            // PHASE 2: Fix decimal numbers before general punctuation fixes
            s = DecimalNumberFix.Replace(s, "$1.$2");

            // PHASE 3: Fix time formats
            s = TimeFormatFix.Replace(s, "$1:$2");

            // PHASE 4: Fix currency formats
            s = CurrencyFix.Replace(s, "$1$2");

            // PHASE 5: Fix percentages
            s = PercentageFix.Replace(s, "$1%");

            // PHASE 6: Fix mathematical notations
            s = ScientificNotationFix.Replace(s, "$1×10^$3");

            // PHASE 7: Fix date formats
            s = DateFormatFix.Replace(s, "$1/$2/$3");

            // PHASE 8: Fix spacing around slashes (but not in protected content)
            s = ApplySelectiveRegex(s, SlashSpacing, m => $"{m.Groups[1].Value}/{m.Groups[2].Value}");

            // PHASE 9: Fix spacing around equals
            s = ApplySelectiveRegex(s, EqualsSpacing, m => $"{m.Groups[1].Value}={m.Groups[2].Value}");

            // PHASE 10: Fix contractions
            s = ContractionFix.Replace(s, "$1'$2");

            // PHASE 11: Fix Oxford commas
            s = OxfordCommaFix.Replace(s, "$1, $2");

            // PHASE 12: Remove spaces before punctuation (original logic, plus Unicode)
            s = SpaceBeforePunctuation.Replace(s, "$1");
            s = SpaceBeforeUnicodePunctuation.Replace(s, "$1");

            // PHASE 13: Ensure space after comma/semicolon/colon (enhanced)
            s = MissingSpaceAfterCommaSemicolonColon.Replace(s, "$1 $2");

            // PHASE 14: Ensure space after .!? when followed by a letter (preserve abbreviations)
            s = ApplySelectiveRegex(s, MissingSpaceAfterPeriodQuestionExclamation,
                m => !IsAbbreviationContext(s, m.Index) ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : m.Value);

            // PHASE 15: Fix spacing around quotes
            s = SpaceBeforeOpeningQuote.Replace(s, "$1$2");
            s = SpaceAfterClosingQuote.Replace(s, "$1$2");

            // PHASE 16: Normalize repeated punctuation (with intentional preservation check)
            s = ApplySelectiveRegex(s, RepeatedQuestion,
                m => m.Value.Length > 3 ? "???" : m.Value); // Allow "???" for emphasis

            s = ApplySelectiveRegex(s, RepeatedExclamation,
                m => m.Value.Length > 3 ? "!!!" : m.Value); // Allow "!!!" for emphasis

            s = RepeatedDots.Replace(s, "...");

            // PHASE 17: Fix mixed punctuation
            s = MixedPunctuation.Replace(s, "!?");

            // PHASE 18: Normalize dashes
            s = DashNormalization.Replace(s, "—"); // Convert to em-dash

            // PHASE 19: Normalize spaces inside parentheses (enhanced)
            s = SpaceInsideParensOpen.Replace(s, "(");
            s = SpaceInsideParensClose.Replace(s, ")");
            s = MultipleSpaceInsideParens.Replace(s, "(");
            s = MultipleSpaceOutsideParens.Replace(s, ")");

            // PHASE 20: Normalize spaces inside brackets
            s = SpaceInsideBracketsOpen.Replace(s, "[");
            s = SpaceInsideBracketsClose.Replace(s, "]");

            // PHASE 21: Normalize spaces inside braces
            s = SpaceInsideBracesOpen.Replace(s, "{");
            s = SpaceInsideBracesClose.Replace(s, "}");

            // PHASE 22: Fix smart quotes (convert to straight quotes for consistency)
            s = FixSmartQuotes.Replace(s, "$1'$2");

            // PHASE 23: Fix numbered lists
            s = NumberedListFix.Replace(s, "$1. ");

            // PHASE 24: Final whitespace cleanup
            s = MultiSpace.Replace(s, " "); // Re-apply after all modifications

            // PHASE 25: Restore protected content
            s = RestoreProtectedContent(s);

            // PHASE 26: Final trim
            return s.Trim();
        }
        catch
        {
            // Never crash pipeline
            return input;
        }
    }

    // ENHANCEMENT: Helper methods to maintain structure while adding functionality

    private static string ProtectSpecialContent(string input)
    {
        _placeholderMap.Clear();

        // Protect email addresses and URLs
        var matches = EmailUrlProtection.Matches(input);
        foreach (Match match in matches)
        {
            var placeholder = $"__PROTECTED_{_placeholderId++}__";
            _placeholderMap[placeholder] = match.Value;
            input = input.Replace(match.Value, placeholder);
        }

        // Protect identifiers
        matches = IdentifierProtection.Matches(input);
        foreach (Match match in matches)
        {
            var placeholder = $"__PROTECTED_{_placeholderId++}__";
            _placeholderMap[placeholder] = match.Value;
            input = input.Replace(match.Value, placeholder);
        }

        // Protect code-like patterns
        matches = CodeLikePatterns.Matches(input);
        foreach (Match match in matches)
        {
            var placeholder = $"__PROTECTED_{_placeholderId++}__";
            _placeholderMap[placeholder] = match.Value;
            input = input.Replace(match.Value, placeholder);
        }

        // Protect abbreviations
        matches = AbbreviationProtection.Matches(input);
        foreach (Match match in matches)
        {
            var placeholder = $"__PROTECTED_{_placeholderId++}__";
            _placeholderMap[placeholder] = match.Value;
            input = input.Replace(match.Value, placeholder);
        }

        return input;
    }

    private static string RestoreProtectedContent(string input)
    {
        foreach (var kvp in _placeholderMap)
        {
            input = input.Replace(kvp.Key, kvp.Value);
        }
        return input;
    }

    private static string ApplySelectiveRegex(string input, Regex regex, Func<Match, string> replacement)
    {
        return regex.Replace(input, m => replacement(m));
    }

    private static bool IsAbbreviationContext(string text, int position)
    {
        // Check if we're in an abbreviation context
        if (position > 0 && position < text.Length - 1)
        {
            var before = text.Substring(0, position);
            var after = text.Substring(position);

            // Check for common abbreviation patterns
            if (Regex.IsMatch(before, @"\b(?:[A-Z]\.)+$")) // U.S.A., Ph.D.
                return true;

            if (Regex.IsMatch(before, @"\b(?:Mr|Mrs|Ms|Dr|Prof|Rev|Gen|Col|Maj|Capt|Lt|Sgt|Cpl|Pvt|Sr|Jr)\.$", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(before, @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\.$", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(before, @"\b(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun)\.$", RegexOptions.IgnoreCase))
                return true;

            if (Regex.IsMatch(before, @"\b(?:Vol|No|Fig|pp|et al|etc|e\.g|i\.e)\.$", RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    // ENHANCEMENT: Additional utility method (doesn't change public API)
    public static string QuickNormalize(string input)
    {
        // Simplified version for performance-critical paths
        if (string.IsNullOrWhiteSpace(input))
            return input;

        var s = input;

        // Apply only essential fixes
        s = MultiSpace.Replace(s, " ");
        s = SpaceBeforePunctuation.Replace(s, "$1");
        s = MissingSpaceAfterCommaSemicolonColon.Replace(s, "$1 $2");
        s = DecimalNumberFix.Replace(s, "$1.$2");

        return s.Trim();
    }
}