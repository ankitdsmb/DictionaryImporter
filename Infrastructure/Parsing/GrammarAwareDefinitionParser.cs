using System;
using System.Threading;
using System.Threading.Tasks;
using DictionaryImporter.Gateway.Grammar.Feature;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing;

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
            return string.Empty;

        sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

        try
        {
            return await grammar.CleanAsync(
                rawDefinition.Trim(),
                applyAutoCorrection: true,
                languageCode: null,
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Parsing grammar correction failed. SourceCode={SourceCode}. Returning original definition.",
                sourceCode);

            return rawDefinition.Trim();
        }
    }
}