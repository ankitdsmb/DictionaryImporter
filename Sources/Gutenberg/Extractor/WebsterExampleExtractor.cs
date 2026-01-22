using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DictionaryImporter.Domain.Models;

namespace DictionaryImporter.Sources.Gutenberg.Extractor
{
    public sealed class WebsterExampleExtractor : IExampleExtractor
    {
        public string SourceCode => "GUT_WEBSTER";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            if (parsed == null)
                return examples;

            if (string.IsNullOrWhiteSpace(parsed.Definition))
                return examples;

            var def = parsed.Definition;

            // 1) Quoted text extraction (more strict)
            var quotedMatches = Regex.Matches(def, @"[""']([^""']{3,})[""']");
            foreach (Match match in quotedMatches)
            {
                var value = match.Groups[1].Value;
                var cleaned = NormalizeExampleForDedupe(value);

                if (string.IsNullOrWhiteSpace(cleaned))
                    continue;

                if (IsPlaceholder(cleaned))
                    continue;

                // prevent very short junk like single word
                if (cleaned.Length < 6)
                    continue;

                examples.Add(cleaned);
            }

            // 2) e.g / for example extraction
            var egMatches = Regex.Matches(
                def,
                @"(?:e\.g\.|for example|ex\.|example:)\s*([^.;\r\n]+)",
                RegexOptions.IgnoreCase);

            foreach (Match match in egMatches)
            {
                var value = match.Groups[1].Value;
                var cleaned = NormalizeExampleForDedupe(value);

                if (string.IsNullOrWhiteSpace(cleaned))
                    continue;

                if (IsPlaceholder(cleaned))
                    continue;

                if (cleaned.Length < 6)
                    continue;

                examples.Add(cleaned);
            }

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
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

        private static string NormalizeExampleForDedupe(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            var t = example.Trim();

            // collapse whitespace
            t = Regex.Replace(t, @"\s+", " ").Trim();

            // normalize apostrophe
            t = t.Replace("’", "'");

            // strip wrapping quotes
            t = t.Trim('\"', '\'', '“', '”', '‘', '’');

            // trim trailing punctuation noise
            t = t.TrimEnd('.', ',', ';', ':');

            // add '.' if looks like a sentence
            if (t.Length > 10 && !t.EndsWith(".") && !t.EndsWith("!") && !t.EndsWith("?"))
                t += ".";

            // bound
            if (t.Length > 800)
                t = t.Substring(0, 800).Trim();

            return t;
        }
    }
}
