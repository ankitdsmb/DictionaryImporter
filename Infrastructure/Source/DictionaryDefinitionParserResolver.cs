using DictionaryImporter.Core.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Source;

public sealed class DictionaryDefinitionParserResolver : IDictionaryDefinitionParserResolver
{
    private readonly Dictionary<string, ISourceDictionaryDefinitionParser> _map;
    private readonly ILogger<DictionaryDefinitionParserResolver> _logger;

    // In DictionaryDefinitionParserResolver constructor
    public DictionaryDefinitionParserResolver(
        IEnumerable<ISourceDictionaryDefinitionParser> parsers,
        ILogger<DictionaryDefinitionParserResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (parsers == null)
        {
            _logger.LogWarning("Parsers collection is null. Creating empty dictionary.");
            _map = new Dictionary<string, ISourceDictionaryDefinitionParser>();
            return;
        }

        try
        {
            // DEBUG: Log all parsers being registered
            foreach (var parser in parsers)
            {
                _logger.LogDebug("Registering parser: {ParserType} for source {SourceCode}",
                    parser.GetType().Name, parser.SourceCode);
            }

            _map = parsers.ToDictionary(x => x.SourceCode);
            _logger.LogInformation("Loaded {Count} parsers: {Sources}",
                _map.Count, string.Join(", ", _map.Keys));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create parser dictionary. Parsers count: {Count}",
                parsers?.Count() ?? 0);
            _map = new Dictionary<string, ISourceDictionaryDefinitionParser>();
        }
    }

    // In DictionaryDefinitionParserResolver.cs - Replace the fallback logic
    public IDictionaryDefinitionParser Resolve(string sourceCode)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            _logger.LogWarning("Attempted to resolve parser with null/empty source code");
            return CreateBilingualFallbackParser(sourceCode);
        }

        if (_map.TryGetValue(sourceCode, out var parser))
        {
            _logger.LogDebug("Resolved parser for source: {SourceCode}", sourceCode);
            return parser;
        }

        _logger.LogWarning(
            "No definition parser registered for source {SourceCode}. Using enhanced fallback.",
            sourceCode);

        // Enhanced fallback that preserves bilingual content
        return CreateBilingualFallbackParser(sourceCode);
    }

    private IDictionaryDefinitionParser CreateBilingualFallbackParser(string sourceCode)
    {
        // Sources that require Chinese preservation
        var bilingualSources = new HashSet<string>
        {
            "ENG_CHN", "CENTURY21", "ENG_COLLINS"
        };

        if (bilingualSources.Contains(sourceCode))
        {
            return new BilingualFallbackParser(_logger);
        }

        return new DefaultDefinitionParser();
    }

    // New class in same file
    private class BilingualFallbackParser(ILogger logger) : IDictionaryDefinitionParser
    {
        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            logger.LogDebug(
                "Using bilingual fallback parser for {Word} ({Source})",
                entry.Word,
                entry.SourceCode);

            // Preserve ALL content for bilingual sources
            var definition = entry.Definition;
            var rawFragment = entry.RawFragmentLine ?? entry.Definition ?? string.Empty;

            // For ENG_CHN with ⬄ separator
            if (entry.SourceCode == "ENG_CHN" && rawFragment.Contains('⬄'))
            {
                var parts = rawFragment.Split('⬄', 2);
                if (parts.Length > 1)
                {
                    definition = parts[1].Trim();
                }
            }

            yield return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = definition ?? string.Empty,
                RawFragment = rawFragment,
                SenseNumber = entry.SourceCode == "ENG_CHN" ? 1 : entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }
    }
}