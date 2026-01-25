using System.Collections.Concurrent;
using DictionaryImporter.Sources.Generic;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry;

public sealed class ExampleExtractorRegistry : IExampleExtractorRegistry
{
    private readonly ConcurrentDictionary<string, IExampleExtractor> _extractors;
    private readonly IExampleExtractor _genericExtractor;
    private readonly ILogger<ExampleExtractorRegistry> _logger;

    public ExampleExtractorRegistry(
        IEnumerable<IExampleExtractor> extractors,
        GenericExampleExtractor genericExtractor,
        ILogger<ExampleExtractorRegistry> logger)
    {
        _extractors = new ConcurrentDictionary<string, IExampleExtractor>();
        _genericExtractor = genericExtractor;
        _logger = logger;

        foreach (var extractor in extractors)
            Register(extractor);
    }

    public IExampleExtractor GetExtractor(string sourceCode)
    {
        if (_extractors.TryGetValue(sourceCode, out var extractor))
            return extractor;

        _logger.LogDebug(
            "No specific extractor found for source {Source}, using generic extractor",
            sourceCode);

        return _genericExtractor;
    }

    public void Register(IExampleExtractor extractor)
    {
        if (extractor.SourceCode == "*")
        {
            _logger.LogWarning(
                "Generic extractor (*) should be registered via constructor, not manually");
            return;
        }

        if (_extractors.TryAdd(extractor.SourceCode, extractor))
        {
            _logger.LogInformation(
                "Registered example extractor for source {Source}",
                extractor.SourceCode);
            return;
        }

        // Duplicate registration is not harmful
        _logger.LogDebug(
            "Example extractor for source {Source} already registered",
            extractor.SourceCode);
    }

    public IReadOnlyDictionary<string, IExampleExtractor> GetAllExtractors()
    {
        return _extractors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}