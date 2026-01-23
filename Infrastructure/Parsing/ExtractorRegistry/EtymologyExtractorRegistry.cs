using System.Collections.Concurrent;
using DictionaryImporter.Sources.Generic;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing.ExtractorRegistry
{
    public sealed class EtymologyExtractorRegistry : IEtymologyExtractorRegistry
    {
        private readonly ConcurrentDictionary<string, IEtymologyExtractor> _extractors;
        private readonly GenericEtymologyExtractor _genericExtractor;
        private readonly ILogger<EtymologyExtractorRegistry> _logger;

        public EtymologyExtractorRegistry(
            IEnumerable<IEtymologyExtractor> extractors,
            GenericEtymologyExtractor genericExtractor,
            ILogger<EtymologyExtractorRegistry> logger)
        {
            _extractors = new ConcurrentDictionary<string, IEtymologyExtractor>();
            _genericExtractor = genericExtractor;
            _logger = logger;

            foreach (var extractor in extractors)
            {
                if (extractor.SourceCode != "*")
                    Register(extractor);
            }
        }

        public IEtymologyExtractor GetExtractor(string sourceCode)
        {
            if (_extractors.TryGetValue(sourceCode, out var extractor))
            {
                _logger.LogDebug(
                    "Using etymology extractor for source {Source}: {ExtractorType}",
                    sourceCode,
                    extractor.GetType().Name);

                return extractor;
            }

            _logger.LogDebug(
                "No specific etymology extractor for source {Source}, using generic",
                sourceCode);

            return _genericExtractor;
        }

        public void Register(IEtymologyExtractor extractor)
        {
            if (extractor.SourceCode == "*")
            {
                _logger.LogWarning("Generic extractor (*) should be registered via constructor");
                return;
            }

            if (_extractors.TryAdd(extractor.SourceCode, extractor))
            {
                _logger.LogInformation(
                    "Registered etymology extractor for source {Source} ({Type})",
                    extractor.SourceCode,
                    extractor.GetType().Name);
                return;
            }

            // Duplicate registration is not harmful
            _logger.LogDebug(
                "Etymology extractor for source {Source} already registered ({Type})",
                extractor.SourceCode,
                extractor.GetType().Name);
        }

        public IReadOnlyDictionary<string, IEtymologyExtractor> GetAllExtractors()
        {
            return new Dictionary<string, IEtymologyExtractor>(_extractors);
        }
    }
}