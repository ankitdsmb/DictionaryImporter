using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Common.Parsing
{
    public sealed class DictionaryDefinitionParserResolver(
        IEnumerable<ISourceDictionaryDefinitionParser> parsers,
        ILogger<DictionaryDefinitionParserResolver> logger)
        : IDictionaryDefinitionParserResolver
    {
        private readonly Dictionary<string, ISourceDictionaryDefinitionParser> _map =
            parsers.ToDictionary(x => x.SourceCode);

        public IDictionaryDefinitionParser Resolve(string sourceCode)
        {
            if (_map.TryGetValue(sourceCode, out var parser))
                return parser;

            logger.LogWarning(
                "No definition parser registered for source {SourceCode}. Using fallback parser.",
                sourceCode);

            return new DefaultDefinitionParser();
        }
    }
}