using DictionaryImporter.Common;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.Oxford.Extractor;

public sealed class OxfordExampleExtractor : IExampleExtractor
{
    public string SourceCode => "ENG_OXFORD";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        var examples = new List<string>();

        if (parsed == null || string.IsNullOrWhiteSpace(parsed.RawFragment))
            return examples;

        var lines = parsed.RawFragment.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inExamplesSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Start of examples section
            if (trimmed.StartsWith("【Examples】", StringComparison.OrdinalIgnoreCase))
            {
                inExamplesSection = true;
                continue;
            }

            if (!inExamplesSection)
                continue;

            // Stop at next section header
            if (trimmed.StartsWith("【"))
                break;

            // Oxford bullet marker
            if (!trimmed.StartsWith("»"))
                continue;

            var example = trimmed[1..].Trim();
            if (string.IsNullOrWhiteSpace(example))
                continue;

            // Reject placeholders
            if (IsPlaceholderExample(example))
                continue;

            // Reject bilingual / non-English examples
            if (ContainsCjk(example))
                continue;

            // Normalize whitespace early
            example = Regex.Replace(example, @"\s+", " ").Trim();

            // ✅ Normalize + validate using shared extensions
            example = example.NormalizeExample();

            if (!example.IsValidExampleSentence())
                continue;

            examples.Add(example);
        }

        return examples
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPlaceholderExample(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim();

        return t.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase)
            || t.Equals("[BILINGUAL_EXAMPLE]", StringComparison.OrdinalIgnoreCase)
            || t.Equals("NON_ENGLISH", StringComparison.OrdinalIgnoreCase)
            || t.Equals("BILINGUAL_EXAMPLE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            // CJK Unified Ideographs
            if (ch >= 0x4E00 && ch <= 0x9FFF)
                return true;

            // Hiragana / Katakana
            if (ch >= 0x3040 && ch <= 0x30FF)
                return true;

            // Hangul
            if (ch >= 0xAC00 && ch <= 0xD7AF)
                return true;
        }

        return false;
    }
}