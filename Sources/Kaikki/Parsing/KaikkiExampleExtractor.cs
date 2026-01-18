using DictionaryImporter.Sources.Kaikki.Helpers;

namespace DictionaryImporter.Infrastructure.Parsing.ExampleExtractor
{
    public sealed class KaikkiExampleExtractor : IExampleExtractor
    {
        private readonly ILogger<KaikkiExampleExtractor> _logger;

        public KaikkiExampleExtractor(ILogger<KaikkiExampleExtractor> logger)
        {
            _logger = logger;
        }

        public string SourceCode => "KAIKKI";

        public IReadOnlyList<string> Extract(ParsedDefinition parsed)
        {
            var examples = new List<string>();

            try
            {
                // Only process English Kaikki entries
                if (string.IsNullOrWhiteSpace(parsed.RawFragment) ||
                    !KaikkiJsonHelper.IsEnglishEntry(parsed.RawFragment))
                {
                    return examples;
                }

                examples = KaikkiJsonHelper.ExtractExamples(parsed.RawFragment);

                // Clean the examples
                for (int i = 0; i < examples.Count; i++)
                {
                    examples[i] = CleanExampleText(examples[i]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract examples from Kaikki JSON");
            }

            return examples
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct()
                .ToList();
        }

        private string CleanExampleText(string example)
        {
            if (string.IsNullOrWhiteSpace(example))
                return string.Empty;

            // Remove quotation marks
            example = example.Trim('"', '\'', '`', '«', '»', '「', '」', '『', '』');

            // Remove translation in parentheses
            example = Regex.Replace(example, @"\s*\([^)]*\)\s*", " ");

            // Ensure proper punctuation
            if (!example.EndsWith(".") && !example.EndsWith("!") && !example.EndsWith("?"))
            {
                example += ".";
            }

            return example.Trim();
        }
    }
}