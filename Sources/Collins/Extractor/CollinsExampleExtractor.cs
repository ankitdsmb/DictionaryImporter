using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;

namespace DictionaryImporter.Sources.Collins.Extractor
{
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

            // ✅ Main extraction using shared helper
            var examples = CollinsParsingHelper.ExtractExamples(raw)
                .Select(NormalizeForDedupe)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Where(e => !IsPlaceholder(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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

        private static string NormalizeForDedupe(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            var t = example.Trim();

            // collapse whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            // normalize apostrophes
            t = t.Replace("’", "'");

            // remove wrapping quotes
            t = t.Trim('\"', '\'', '“', '”', '‘', '’');

            // remove trailing punctuation duplicates
            t = t.TrimEnd('.', ',', ';', ':');

            // add a sentence end if missing
            if (!t.EndsWith(".") && !t.EndsWith("!") && !t.EndsWith("?"))
                t += ".";

            if (t.Length > 800)
                t = t.Substring(0, 800).Trim();

            return t;
        }
    }
}
