using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Common.Parsing
{
    public sealed class DictionaryDefinitionParserResolver : IDictionaryDefinitionParserResolver
    {
        private readonly Dictionary<string, ISourceDictionaryDefinitionParser> _map;
        private readonly ILogger<DictionaryDefinitionParserResolver> _logger;

        public DictionaryDefinitionParserResolver(
            IEnumerable<ISourceDictionaryDefinitionParser> parsers,
            ILogger<DictionaryDefinitionParserResolver> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // FIX: Handle null or empty parsers collection
            if (parsers == null)
            {
                _logger.LogWarning("Parsers collection is null. Creating empty dictionary.");
                _map = new Dictionary<string, ISourceDictionaryDefinitionParser>();
                return;
            }

            try
            {
                _map = parsers.ToDictionary(x => x.SourceCode);
                _logger.LogDebug("Loaded {Count} parsers: {Sources}",
                    _map.Count, string.Join(", ", _map.Keys));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create parser dictionary");
                _map = new Dictionary<string, ISourceDictionaryDefinitionParser>();
            }
        }

        public IDictionaryDefinitionParser Resolve(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
            {
                _logger.LogWarning("Attempted to resolve parser with null/empty source code");
                return new DefaultDefinitionParser();
            }

            if (_map.TryGetValue(sourceCode, out var parser))
            {
                _logger.LogDebug("Resolved parser for source: {SourceCode}", sourceCode);
                return parser;
            }

            _logger.LogWarning(
                "No definition parser registered for source {SourceCode}. Using fallback parser.",
                sourceCode);

            return new DefaultDefinitionParser();
        }

        // Enhanced fallback parser
        private class EnhancedFallbackParser : IDictionaryDefinitionParser
        {
            private readonly ILogger _logger;

            public EnhancedFallbackParser(ILogger logger)
            {
                _logger = logger;
            }

            public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
            {
                _logger.LogDebug("Using fallback parser for {Word} ({Source})",
                    entry.Word, entry.SourceCode);

                // Preserve ALL content for bilingual sources
                var definition = entry.Definition;

                if (entry.SourceCode == "ENG_CHN" || entry.SourceCode == "CENTURY21")
                {
                    // Extract Chinese part if present
                    var idx = definition?.IndexOf('⬄') ?? -1;
                    if (idx >= 0 && idx < definition!.Length - 1)
                    {
                        definition = definition[(idx + 1)..].Trim();
                    }
                }

                yield return new ParsedDefinition
                {
                    MeaningTitle = entry.Word ?? "unnamed sense",
                    Definition = definition ?? string.Empty,
                    RawFragment = entry.RawFragment ?? definition ?? string.Empty,
                    SenseNumber = entry.SenseNumber,
                    Domain = null,
                    UsageLabel = null,
                    CrossReferences = new List<CrossReference>(),
                    Synonyms = null,
                    Alias = null
                };
            }
        }
    }
}