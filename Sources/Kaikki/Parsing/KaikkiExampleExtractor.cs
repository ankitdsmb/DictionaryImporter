using System.Text.Json;
using DictionaryImporter.Sources.Common.Helper;
using JsonException = Newtonsoft.Json.JsonException;

namespace DictionaryImporter.Sources.Kaikki.Parsing
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
                if (string.IsNullOrWhiteSpace(parsed.RawFragment))
                    return examples;

                using var doc = JsonDocument.Parse(parsed.RawFragment);
                var root = doc.RootElement;

                if (!JsonProcessor.IsEnglishEntry(root))
                    return examples;

                examples = SourceDataHelper.ExtractExamples(parsed.RawFragment);

                for (var i = 0; i < examples.Count; i++)
                {
                    examples[i] = SourceDataHelper.CleanExampleText(examples[i]);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse Kaikki JSON for example extraction");
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
    }
}