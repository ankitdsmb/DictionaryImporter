using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Core.Abstractions;

public interface ISynonymExtractor
{
    string SourceCode { get; }

    IReadOnlyList<SynonymDetectionResult> Extract(
        string headword,
        string definition,
        string? rawDefinition = null);

    bool ValidateSynonymPair(string headwordA, string headwordB);
}