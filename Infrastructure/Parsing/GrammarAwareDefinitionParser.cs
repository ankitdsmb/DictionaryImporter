// File: DictionaryImporter.Infrastructure/Parsing/GrammarAwareDefinitionParser.cs
using DictionaryImporter.Core.Grammar;

namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class GrammarAwareDefinitionParser : IDictionaryDefinitionParser
{
    private readonly IDictionaryDefinitionParser _innerParser;
    private readonly IGrammarCorrector _grammarCorrector;
    private readonly ILogger<GrammarAwareDefinitionParser> _logger;

    public GrammarAwareDefinitionParser(
        IDictionaryDefinitionParser innerParser,
        IGrammarCorrector grammarCorrector = null,
        ILogger<GrammarAwareDefinitionParser> logger = null)
    {
        _innerParser = innerParser ?? throw new ArgumentNullException(nameof(innerParser));
        _grammarCorrector = grammarCorrector;
        _logger = logger;
    }

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        var parsedDefinitions = _innerParser.Parse(entry);

        foreach (var parsed in parsedDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(parsed.Definition))
            {
                parsed.Definition = ApplyGrammarImprovements(parsed.Definition).GetAwaiter().GetResult();
            }

            // Also improve examples if present
            if (parsed.Examples?.Any() == true)
            {
                var improvedExamples = new List<string>();
                foreach (var example in parsed.Examples)
                {
                    improvedExamples.Add(ApplyGrammarImprovements(example).GetAwaiter().GetResult());
                }
                parsed.Examples = improvedExamples;
            }

            yield return parsed;
        }
    }

    private async Task<string> ApplyGrammarImprovements(string text)
    {
        if (_grammarCorrector == null || string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            // Only apply corrections for definitions over a certain length
            if (text.Length < 10)
                return text;

            var result = await _grammarCorrector.AutoCorrectAsync(text, "en-US");

            if (result.AppliedCorrections.Any())
            {
                _logger?.LogDebug(
                    "Applied {Count} grammar corrections to definition text",
                    result.AppliedCorrections.Count
                );
            }

            return result.CorrectedText;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to apply grammar corrections to text");
            return text;
        }
    }
}