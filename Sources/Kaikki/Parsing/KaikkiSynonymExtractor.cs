using DictionaryImporter.Common;
using DictionaryImporter.Common.SourceHelper;

namespace DictionaryImporter.Sources.Kaikki.Parsing
{
    internal class KaikkiSynonymExtractor : ISynonymExtractor
    {
        private readonly ILogger<KaikkiSynonymExtractor> _logger;

        public KaikkiSynonymExtractor(ILogger<KaikkiSynonymExtractor> logger)
        {
            _logger = logger;
        }

        public string SourceCode => "KAIKKI";

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

                if (!ParsingHelperKaikki.TryParseEnglishRoot(rawDefinition, out _))
                    return results;

                var synonyms = ParsingHelperKaikki.ExtractSynonyms(rawDefinition);

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
                        DetectionMethod = "KaikkiStructuredSynonym",
                        SourceText = $"Kaikki synonym: {synonym}"
                    });
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                _logger.LogDebug(ex, "Failed to parse Kaikki JSON for synonym extraction | Headword={Headword}", headword);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to extract synonyms for {Headword}", headword);
            }

            return results;
        }

        public bool ValidateSynonymPair(string headwordA, string headwordB)
        {
            return ParsingHelperKaikki.ValidateSynonymPair(headwordA, headwordB);
        }
    }
}
