using System;
using System.Collections.Generic;
using System.Linq;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.EnglishChinese.Extractor;

public sealed class EnglishChineseExampleExtractor : IExampleExtractor
{
    public string SourceCode => "ENG_CHN";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        var examples = new List<string>();

        if (parsed == null)
            return examples;

        if (string.IsNullOrWhiteSpace(parsed.Definition))
            return examples;

        var definition = parsed.Definition;

        // Use the helper class to parse the English-Chinese entry
        var parsedData = ParsingHelperEnglishChinese.ParseEngChnEntry(definition);

        // Get examples from the parsed data
        examples.AddRange(parsedData.Examples);

        // Also get examples from additional senses if present
        if (parsedData.AdditionalSenses != null && parsedData.AdditionalSenses.Count > 0)
        {
            foreach (var sense in parsedData.AdditionalSenses)
            {
                if (sense.Examples != null && sense.Examples.Count > 0)
                {
                    examples.AddRange(sense.Examples);
                }
            }
        }

        // Fallback: if helper didn't find examples, use original logic as backup
        if (examples.Count == 0)
        {
            examples.AddRange(ExtractExamplesFallback(definition));
        }

        // Normalize and deduplicate
        return examples
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(NormalizeExampleForDedupe)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Where(e => !IsPlaceholder(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> ExtractExamplesFallback(string definition)
    {
        var examples = new List<string>();

        if (string.IsNullOrWhiteSpace(definition))
            return examples;

        // Original logic as fallback
        var chineseMarkers = new[] { "例如", "比如", "例句", "例子" };

        foreach (var marker in chineseMarkers)
        {
            var startIndex = definition.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
                continue;

            var tail = definition.Substring(startIndex + marker.Length).Trim();
            if (string.IsNullOrWhiteSpace(tail))
                continue;

            var endIndex = tail.IndexOfAny(new[] { '。', '.', ';', '；', '，', ',', '\n', '\r' });
            if (endIndex > 0)
                tail = tail.Substring(0, endIndex);

            if (!string.IsNullOrWhiteSpace(tail))
                examples.Add(tail);
        }

        return examples;
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

    private static string NormalizeExampleForDedupe(string example)
    {
        if (string.IsNullOrWhiteSpace(example))
            return string.Empty;

        var t = example.Trim();

        // Use helper's CleanExampleText method if available
        // Otherwise use original normalization
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
        t = t.Replace("’", "'");
        t = t.Trim('\"', '\'', '“', '”', '‘', '’');
        t = t.TrimEnd('.', ',', ';', ':', '。', '，', '；');

        if (t.Length < 3)
            return string.Empty;

        if (t.Length > 800)
            t = t.Substring(0, 800).Trim();

        return t;
    }
}