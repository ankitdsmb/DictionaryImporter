using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;
using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Sources.EnglishChinese.Extractor;

public sealed class EnglishChineseExampleExtractor : IExampleExtractor
{
    public string SourceCode => "ENG_CHN";

    public IReadOnlyList<string> Extract(ParsedDefinition parsed)
    {
        if (parsed == null)
            return Array.Empty<string>();

        var raw = parsed.RawFragment;
        if (string.IsNullOrWhiteSpace(raw))
            raw = parsed.Definition;

        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var examples = new List<string>();

        // 1️⃣ Primary: structured English–Chinese parsing
        var parsedData = ParsingHelperEnglishChinese.ParseEngChnEntry(raw);

        if (parsedData?.Examples != null)
            examples.AddRange(parsedData.Examples);

        if (parsedData?.AdditionalSenses != null)
        {
            foreach (var sense in parsedData.AdditionalSenses)
            {
                if (sense?.Examples != null)
                    examples.AddRange(sense.Examples);
            }
        }

        // 2️⃣ Fallback only if structured parsing found nothing
        if (examples.Count == 0)
            examples.AddRange(ExtractExamplesFallback(raw));

        // 3️⃣ Normalize + validate (shared logic)
        return examples
            .Select(e => e.NormalizeExample())          // ✅ shared normalization
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Where(e => !IsPlaceholder(e))
            .Where(e => e.IsValidExampleSentence())     // ✅ semantic filter
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ExtractExamplesFallback(string text)
    {
        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return results;

        var markers = new[] { "例如", "比如", "例句", "例子" };

        foreach (var marker in markers)
        {
            var idx = text.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            var tail = text[(idx + marker.Length)..].Trim();
            if (string.IsNullOrWhiteSpace(tail))
                continue;

            var cut = tail.IndexOfAny(new[] { '。', '.', ';', '；', '\n', '\r' });
            if (cut > 0)
                tail = tail[..cut];

            if (!string.IsNullOrWhiteSpace(tail))
                results.Add(tail);
        }

        return results;
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