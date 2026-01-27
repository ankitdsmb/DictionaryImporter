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

        var examples = ParsingHelperCollins.ExtractExamples(raw)
            .Select(e => e.NormalizeExample())          // 🔴 unified normalization
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Where(e => !IsPlaceholder(e))
            .Where(e => e.IsValidExampleSentence())     // 🔴 semantic filter
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return examples
            .Select(e => e.NormalizeExample())
            .Where(e => e.IsValidExampleSentence())
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
               || text.Equals("[BILINGUAL_EXAMPLE]", StringComparison.OrdinalIgnoreCase);
    }
}