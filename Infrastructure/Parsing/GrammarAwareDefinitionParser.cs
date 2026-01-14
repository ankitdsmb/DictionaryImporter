namespace DictionaryImporter.Infrastructure.Parsing;

public sealed class GrammarAwareDefinitionParser(
    IDictionaryDefinitionParser innerParser,
    IGrammarCorrector grammarCorrector = null,
    ILogger<GrammarAwareDefinitionParser> logger = null)
    : IDictionaryDefinitionParser
{
    private readonly IDictionaryDefinitionParser _innerParser = innerParser ?? throw new ArgumentNullException(nameof(innerParser));

    public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
    {
        var parsedDefinitions = _innerParser.Parse(entry);

        foreach (var parsed in parsedDefinitions)
        {
            if (!string.IsNullOrWhiteSpace(parsed.Definition))
            {
                parsed.Definition = ApplyGrammarImprovements(parsed.Definition).GetAwaiter().GetResult();
            }

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
        if (grammarCorrector == null || string.IsNullOrWhiteSpace(text))
            return text;

        try
        {
            if (text.Length < 10)
                return text;

            var result = await grammarCorrector.AutoCorrectAsync(text, "en-US");

            if (result.AppliedCorrections.Any())
            {
                logger?.LogDebug(
                    "Applied {Count} grammar corrections to definition text",
                    result.AppliedCorrections.Count
                );
            }

            return result.CorrectedText;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to apply grammar corrections to text");
            return text;
        }
    }
}