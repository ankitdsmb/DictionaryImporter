namespace DictionaryImporter.Core.Parsing
{
    public interface IEtymologyExtractor
    {
        string SourceCode { get; }

        EtymologyExtractionResult Extract(
            string headword,
            string definition,
            string? rawDefinition = null);

        (string? Etymology, string? LanguageCode) ExtractFromText(string text);
    }
    public interface IEtymologyExtractorRegistry
    {
        IEtymologyExtractor GetExtractor(string sourceCode);
        void Register(IEtymologyExtractor extractor);
        IReadOnlyDictionary<string, IEtymologyExtractor> GetAllExtractors();
    }

    public sealed class EtymologyExtractionResult
    {
        public string? EtymologyText { get; init; }
        public string? LanguageCode { get; init; }
        public string? CleanedDefinition { get; init; } // Definition with etymology removed
        public string DetectionMethod { get; init; } = null!;
        public string SourceText { get; init; } = null!;
    }
}