namespace DictionaryImporter.Infrastructure.Parsing.SynonymExtractor
{
    internal class WebsterSynonymExtractor : ISynonymExtractor
    {
        string ISynonymExtractor.SourceCode => "GUT_WEBSTER";

        IReadOnlyList<SynonymDetectionResult> ISynonymExtractor.Extract(string headword, string definition,
            string? rawDefinition)
        {
            return Sources.Gutenberg.Parsing.WebsterSynonymExtractor.Extract(definition)
                .Where(s => !s.Equals(headword, StringComparison.OrdinalIgnoreCase))
                .Select(s => new SynonymDetectionResult
                {
                    TargetHeadword = s,
                    ConfidenceLevel = "High",
                    DetectionMethod = "WebsterSynonymPattern",
                    SourceText = definition ?? string.Empty
                })
                .ToList();
        }

        bool ISynonymExtractor.ValidateSynonymPair(string headwordA, string headwordB)
        {
            return false;
        }
    }
}