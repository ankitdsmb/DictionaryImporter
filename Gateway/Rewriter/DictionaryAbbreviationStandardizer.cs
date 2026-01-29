namespace DictionaryImporter.Gateway.Rewriter;

// NEW CLASS (added)
public static class DictionaryAbbreviationStandardizer
{
    private const RegexOptions Options =
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private const int MaxInputLength = 260;

    // Very small deterministic whitelist of known dictionary abbreviations.
    // RewriteMap should do most work; this only fixes safe leftovers.
    private static readonly AbbreviationRule[] Rules =
    [
        new("adj", "adj."),
        new("v", "v."),
        new("n", "n."),
        new("pl", "pl."),
        new("fig", "fig."),
        new("obs", "obs."),
    ];

    private readonly record struct AbbreviationRule(string Token, string Standard);

    // NEW METHOD (added)
    public static AbbreviationStandardizeResult StandardizeSafe(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return AbbreviationStandardizeResult.NoChange(input ?? string.Empty);

        var text = input.Trim();

        // Safety: skip large paragraphs; this is label-scope normalization only.
        if (text.Length > MaxInputLength)
            return AbbreviationStandardizeResult.NoChange(text);

        // Safety: do not interfere with protected token placeholders (⟦PT000001⟧)
        if (text.IndexOf("⟦PT", StringComparison.Ordinal) >= 0)
            return AbbreviationStandardizeResult.NoChange(text);

        // Safety: avoid rewriting non-ascii letter content
        if (ContainsNonAsciiLetters(text))
            return AbbreviationStandardizeResult.NoChange(text);

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = text;

        try
        {
            foreach (var rule in Rules)
            {
                // Match whole token as a standalone label or within a short label list.
                // Examples:
                // "adj" -> "adj."
                // "(adj)" -> "(adj.)"
                // "adj, fig" -> "adj., fig."
                // "adj: ..." -> "adj.: ..."
                //
                // Not allowed:
                // "adjective" (no match)
                // "nav" (no match)
                // "obsidian" (no match)
                var pattern = $@"(?<=^|[\s\(\[\{{,/;:])({Regex.Escape(rule.Token)})(?=$|[\s\)\]\}}.,;:])";
                var regex = new Regex(pattern, Options, TimeSpan.FromMilliseconds(40));

                if (!regex.IsMatch(current))
                    continue;

                var replaced = regex.Replace(current, rule.Standard);

                if (!string.Equals(replaced, current, StringComparison.Ordinal))
                {
                    current = replaced;
                    applied.Add(rule.Token);
                }
            }

            // Fix accidental "adj.." -> "adj."
            current = Regex.Replace(
                current,
                @"\b(adj|v|n|pl|fig|obs)\.\.+\b",
                "$1.",
                Options,
                TimeSpan.FromMilliseconds(40));

            // If abbreviation is followed by a letter, ensure spacing: "adj.Def" -> "adj. Def"
            current = Regex.Replace(
                current,
                @"\b(adj|v|n|pl|fig|obs)\.\s*(?=[A-Za-z])",
                "$1. ",
                Options,
                TimeSpan.FromMilliseconds(40));
        }
        catch
        {
            return AbbreviationStandardizeResult.NoChange(text);
        }

        if (string.Equals(current, text, StringComparison.Ordinal) || applied.Count == 0)
            return AbbreviationStandardizeResult.NoChange(text);

        var appliedList = applied
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AbbreviationStandardizeResult(
            Changed: true,
            Text: current,
            AppliedKeys: appliedList);
    }

    private static bool ContainsNonAsciiLetters(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c <= 0x7F)
                continue;

            if (char.IsLetter(c))
                return true;
        }

        return false;
    }
}

// NEW CLASS (added)