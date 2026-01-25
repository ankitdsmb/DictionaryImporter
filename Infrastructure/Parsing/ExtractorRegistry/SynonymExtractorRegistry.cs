using System.Collections.Concurrent;
using DictionaryImporter.Sources.Generic;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry;

public sealed class SynonymExtractorRegistry : ISynonymExtractorRegistry
{
    private readonly ConcurrentDictionary<string, ISynonymExtractor> _extractors;
    private readonly GenericSynonymExtractor _genericExtractor;
    private readonly ILogger<SynonymExtractorRegistry> _logger;

    public SynonymExtractorRegistry(
        IEnumerable<ISynonymExtractor> extractors,
        GenericSynonymExtractor genericExtractor,
        ILogger<SynonymExtractorRegistry> logger)
    {
        _extractors = new ConcurrentDictionary<string, ISynonymExtractor>();
        _genericExtractor = genericExtractor;
        _logger = logger;

        foreach (var extractor in extractors)
            if (extractor.SourceCode != "*")
                Register(extractor);
    }

    public ISynonymExtractor GetExtractor(string sourceCode)
    {
        if (_extractors.TryGetValue(sourceCode, out var extractor))
        {
            _logger.LogDebug(
                "Using synonym extractor for source {Source}: {ExtractorType}",
                sourceCode,
                extractor.GetType().Name);

            return extractor;
        }

        _logger.LogDebug(
            "No specific synonym extractor for source {Source}, using generic",
            sourceCode);

        return _genericExtractor;
    }

    public void Register(ISynonymExtractor extractor)
    {
        if (extractor.SourceCode == "*")
        {
            _logger.LogWarning("Generic extractor (*) should be registered via constructor");
            return;
        }

        if (_extractors.TryAdd(extractor.SourceCode, extractor))
        {
            _logger.LogInformation(
                "Registered synonym extractor for source {Source} ({Type})",
                extractor.SourceCode,
                extractor.GetType().Name);
            return;
        }

        // Duplicate registration is not harmful
        _logger.LogDebug(
            "Synonym extractor for source {Source} already registered ({Type})",
            extractor.SourceCode,
            extractor.GetType().Name);
    }

    public IReadOnlyDictionary<string, ISynonymExtractor> GetAllExtractors()
    {
        return new Dictionary<string, ISynonymExtractor>(_extractors);
    }
}