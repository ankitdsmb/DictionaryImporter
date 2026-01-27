using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Collins.Extractor;

public sealed class CollinsExampleExtractor : IExampleExtractor
{
    private static readonly Regex ExamplePattern = new(
        @"^[A-Z][^.!?]*[.!?]$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex EllipsisExamplePattern = new(
        @"^\.\.\.\s*[A-Z].*[.!?]$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public string SourceCode => "ENG_COLLINS";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        if (parsed == null)
            return new List<string>();

        // ✅ Always prefer RawFragment for Collins
        var raw = parsed.RawFragment;
        if (string.IsNullOrWhiteSpace(raw))
            raw = parsed.Definition;

        if (string.IsNullOrWhiteSpace(raw))
            return new List<string>();

        // Use both parsing methods
        var examples = new List<string>();

        // Method 1: Use ParsingHelperCollins for structured examples
        var helperExamples = ParsingHelperCollins.ExtractExamples(raw)
            .Select(e => e.NormalizeExample())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Where(e => !IsPlaceholder(e))
            .Where(e => e.IsValidExampleSentence())
            .ToList();

        examples.AddRange(helperExamples);

        // Method 2: Extract additional examples from raw text
        var lines = raw.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Check for example patterns
            if (ExamplePattern.IsMatch(trimmed) &&
                !ContainsChinese(trimmed) &&
                trimmed.Length > 10 && // Reasonable minimum length
                !helperExamples.Any(e => e.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                examples.Add(trimmed.NormalizeExample());
            }
            // Check for ellipsis examples
            else if (EllipsisExamplePattern.IsMatch(trimmed))
            {
                var example = trimmed.Substring(3).Trim(); // Remove "..."
                if (!string.IsNullOrWhiteSpace(example) &&
                    example.IsValidExampleSentence())
                {
                    examples.Add(example.NormalizeExample());
                }
            }
        }

        // Clean and deduplicate
        return examples
            .Select(e => e.NormalizeExample())
            .Where(e => e.IsValidExampleSentence())
            .Where(e => !IsPlaceholder(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPlaceholder(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        return text.StartsWith("[NON_ENGLISH_", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[BILINGUAL_", StringComparison.OrdinalIgnoreCase)
               || text.Equals("[NON_ENGLISH]", StringComparison.OrdinalIgnoreCase)
               || text.Equals("[BILINGUAL_EXAMPLE]", StringComparison.OrdinalIgnoreCase)
               || text.Length < 5; // Too short to be meaningful
    }

    private static bool ContainsChinese(string text)
    {
        return Regex.IsMatch(text, @"[\u4E00-\u9FFF]");
    }
}