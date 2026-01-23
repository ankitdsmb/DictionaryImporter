using DictionaryImporter.Common;
using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources.Gutenberg.Extractor
{
    internal sealed class GutenbergSynonymExtractor : ISynonymExtractor
    {
        private readonly ILogger<GutenbergSynonymExtractor> _logger;

        public GutenbergSynonymExtractor(ILogger<GutenbergSynonymExtractor> logger)
        {
            _logger = logger;
        }

        public string SourceCode => "GUT_WEBSTER";

        public IReadOnlyList<SynonymDetectionResult> Extract(
            string headword,
            string definition,
            string? rawDefinition = null)
        {
            var results = new List<SynonymDetectionResult>();

            try
            {
                if (string.IsNullOrWhiteSpace(rawDefinition))
                    return results;

                var synonyms = ParsingHelperGutenberg.ExtractSynonyms(rawDefinition);

                foreach (var synonym in synonyms.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!ValidateSynonymPair(headword, synonym))
                        continue;

                    var normalizedTarget = Helper.NormalizeWord(synonym);

                    if (string.IsNullOrWhiteSpace(normalizedTarget))
                        continue;

                    results.Add(new SynonymDetectionResult
                    {
                        TargetHeadword = normalizedTarget,
                        ConfidenceLevel = "high",
                        DetectionMethod = "GutenbergSynonymSection",
                        SourceText = $"Gutenberg synonym: {synonym}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract Gutenberg synonyms | Headword={Headword}", headword);
            }

            return results;
        }

        public bool ValidateSynonymPair(string headwordA, string headwordB)
        {
            return ParsingHelperGutenberg.ValidateSynonymPair(headwordA, headwordB);
        }
    }
}
