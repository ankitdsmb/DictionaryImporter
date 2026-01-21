using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Collins.Extractor
{
    public sealed class CollinsExampleExtractor : IExampleExtractor
    {
        public string SourceCode => "ENG_COLLINS";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(parsed.RawFragment) &&
                string.IsNullOrWhiteSpace(parsed.Definition))
                return examples;

            // PRIMARY FIX: Use RawFragment (contains full Collins format)
            var rawFragment = parsed.RawFragment ?? parsed.Definition;

            // Method 1: Extract from "【Examples】" section
            if (!string.IsNullOrWhiteSpace(rawFragment) && rawFragment.Contains("【Examples】"))
            {
                var exampleSection = ExtractExampleSection(rawFragment);
                examples.AddRange(exampleSection);
            }

            // Method 2: Use CollinsSourceDataHelper (backup)
            var helperExamples = CollinsSourceDataHelper.ExtractExamples(rawFragment ?? "");
            examples.AddRange(helperExamples);

            // Method 3: Extract inline examples (sentences in quotes or starting with capital)
            var inlineExamples = ExtractInlineExamples(rawFragment ?? "");
            examples.AddRange(inlineExamples);

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(CleanExample)
                .Distinct()
                .ToList();
        }

        private List<string> ExtractExampleSection(string text)
        {
            var examples = new List<string>();

            try
            {
                var exampleMarker = "【Examples】";
                var startIndex = text.IndexOf(exampleMarker);
                if (startIndex >= 0)
                {
                    startIndex += exampleMarker.Length;

                    // Find end of example section (next marker or end)
                    var endIndex = text.IndexOf("【", startIndex);
                    if (endIndex < 0) endIndex = text.Length;

                    var exampleText = text.Substring(startIndex, endIndex - startIndex).Trim();

                    // Collins examples are bulleted: • example 1 • example 2
                    var lines = exampleText.Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("•"))
                        {
                            var example = trimmed.Substring(1).Trim();
                            if (!string.IsNullOrWhiteSpace(example))
                                examples.Add(example);
                        }
                        else if (trimmed.Length > 10 && char.IsUpper(trimmed[0]))
                        {
                            // Might be example without bullet
                            examples.Add(trimmed);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // If parsing fails, return empty
            }

            return examples;
        }

        private List<string> ExtractInlineExamples(string text)
        {
            var examples = new List<string>();

            // Look for English sentences that might be examples
            // Patterns: "For example, ..." or "e.g., ..." or quoted text
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+");

            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();

                // Check if this looks like an example
                if (IsLikelyExample(trimmed))
                {
                    examples.Add(trimmed);
                }
            }

            return examples;
        }

        private bool IsLikelyExample(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
                return false;

            // Example indicators
            var indicators = new[]
            {
                "for example",
                "e.g.",
                "for instance",
                "such as",
                "like",
                "including"
            };

            var lowerText = text.ToLowerInvariant();
            foreach (var indicator in indicators)
            {
                if (lowerText.Contains(indicator))
                    return true;
            }

            // Check if it's a quoted sentence
            if (text.StartsWith("\"") && text.EndsWith("\"") ||
                text.StartsWith("'") && text.EndsWith("'"))
            {
                return true;
            }

            return false;
        }

        private string CleanExample(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return example;

            // Remove excessive whitespace
            example = Regex.Replace(example, @"\s+", " ").Trim();

            // Ensure proper punctuation
            if (!example.EndsWith(".") && !example.EndsWith("!") && !example.EndsWith("?"))
            {
                example += ".";
            }

            return example;
        }
    }
}