using System.Text.RegularExpressions;

namespace DictionaryImporter.Core.Text;

public sealed class DefinitionNormalizer : IDefinitionNormalizer
{
    public string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        // 1) Normalize whitespace
        raw = Regex.Replace(raw, @"[ \t]+", " ");
        raw = raw.Replace("\r\n", "\n").Replace("\r", "\n");

        // 2) Split into candidate lines (meanings)
        var lines = raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToList();

        if (lines.Count <= 1)
        {
            return CleanupSentence(raw.Trim());
        }

        // 3) Clean each meaning line
        var cleaned = new List<string>(lines.Count);

        foreach (var line in lines)
        {
            var item = CleanupSentence(line);

            // Skip extremely short junk
            if (item.Length < 2)
                continue;

            cleaned.Add(item);
        }

        // 4) Merge as numbered meanings
        // We store as multi-line formatted text (stable)
        var result = string.Join("\n", cleaned.Select((x, i) => $"{i + 1}) {x}"));

        return result.Trim();
    }

    private static string CleanupSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Fix broken spacing around punctuation
        text = Regex.Replace(text, @"\s+([,.;:!?])", "$1");
        text = Regex.Replace(text, @"([(\[])\s+", "$1");
        text = Regex.Replace(text, @"\s+([)\]])", "$1");

        // Normalize quotes (optional basic cleanup)
        text = text.Replace("“", "\"").Replace("”", "\"").Replace("’", "'");

        // Fix repeated separators
        text = Regex.Replace(text, @";\s*;", "; ");
        text = Regex.Replace(text, @"\.\s*\.", ".");

        // Trim
        return text.Trim();
    }
}