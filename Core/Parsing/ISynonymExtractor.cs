namespace DictionaryImporter.Core.Parsing
{
    public interface ISynonymExtractor
    {
        string SourceCode { get; }

        IReadOnlyList<SynonymDetectionResult> Extract(
            string headword,
            string definition,
            string? rawDefinition = null);

        bool ValidateSynonymPair(string headwordA, string headwordB);
    }
}