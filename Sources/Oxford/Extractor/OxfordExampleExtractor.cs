// File: Sources/Oxford/Extractor/OxfordExampleExtractor.cs

namespace DictionaryImporter.Sources.Oxford.Extractor
{
    public sealed class OxfordExampleExtractor : IExampleExtractor
    {
        public string SourceCode => "ENG_OXFORD";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            if (parsed == null)
                return examples;

            if (string.IsNullOrWhiteSpace(parsed.RawFragment))
                return examples;

            var lines = parsed.RawFragment.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var inExamplesSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("【Examples】", StringComparison.OrdinalIgnoreCase))
                {
                    inExamplesSection = true;
                    continue;
                }

                if (!inExamplesSection)
                    continue;

                // stop on next section or blank
                if (trimmed.StartsWith("【") || string.IsNullOrWhiteSpace(trimmed))
                    break;

                if (!trimmed.StartsWith("»"))
                    continue;

                var example = trimmed.Substring(1).Trim();

                if (string.IsNullOrWhiteSpace(example))
                    continue;

                // FIX: never allow placeholder examples
                if (IsPlaceholderExample(example))
                    continue;

                // FIX: skip bilingual/non-English example lines (they don't belong in DictionaryEntryExample.ExampleText)
                if (ContainsCjk(example))
                    continue;

                // Normalize whitespace
                example = Regex.Replace(example, @"\s+", " ").Trim();

                if (example.Length == 0)
                    continue;

                examples.Add(example);
            }

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
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
            // Chinese / Japanese / Korean Unicode ranges (simple, fast check)
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
}
