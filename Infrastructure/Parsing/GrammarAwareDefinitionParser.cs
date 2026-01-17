using DictionaryImporter.AITextKit.Grammar.Feature;

public sealed class GrammarAwareDefinitionParser(
    IGrammarFeature grammar,
    ILogger<GrammarAwareDefinitionParser> logger)
{
    public async Task<string> ParseDefinitionAsync(
        string rawDefinition,
        string sourceCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawDefinition))
            return rawDefinition;

        try
        {
            // Parsing-time correction (clean + autocorrect)
            return await grammar.CleanAsync(
                rawDefinition,
                applyAutoCorrection: true,
                languageCode: null,
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Parsing grammar correction failed. Returning original definition.");
            return rawDefinition;
        }
    }
}