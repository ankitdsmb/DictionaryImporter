using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Text;

public interface IGrammarEnrichedTextService
{
    Task<string> NormalizeDefinitionAsync(string raw, CancellationToken ct);

    Task<string> NormalizeExampleAsync(string raw, CancellationToken ct);
}

public sealed class GrammarEnrichedTextService(
    IGrammarFeature grammar,
    IOcrArtifactNormalizer ocrNormalizer,
    IDefinitionNormalizer definitionNormalizer,
    ILogger<GrammarEnrichedTextService> logger) : IGrammarEnrichedTextService
{
    public async Task<string> NormalizeDefinitionAsync(string raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        try
        {
            var ocrFixed = ocrNormalizer.Normalize(raw);
            var normalized = definitionNormalizer.Normalize(ocrFixed);
            return await grammar.CleanAsync(
                normalized,
                applyAutoCorrection: true,
                languageCode: null,
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Definition normalization failed. Returning original text.");
            return raw;
        }
    }

    public async Task<string> NormalizeExampleAsync(string raw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        try
        {
            return await grammar.CleanAsync(
                raw,
                applyAutoCorrection: true,
                languageCode: null,
                ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Example normalization failed. Returning original text.");
            return raw;
        }
    }
}