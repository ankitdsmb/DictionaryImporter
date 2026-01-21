// File: Core/Text/GrammarEnrichedTextService.cs
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Core.Text
{
    public interface IGrammarEnrichedTextService
    {
        Task<string> NormalizeDefinitionAsync(string definition, CancellationToken ct);
        Task<string> NormalizeExampleAsync(string example, CancellationToken ct);
    }

    public class GrammarEnrichedTextService(ILogger<GrammarEnrichedTextService> logger) : IGrammarEnrichedTextService
    {
        public Task<string> NormalizeDefinitionAsync(string definition, CancellationToken ct)
        {
            // Basic normalization - implement your grammar correction logic here
            if (string.IsNullOrWhiteSpace(definition))
                return Task.FromResult(string.Empty);

            var normalized = definition.Trim();
            logger.LogDebug("Normalized definition: {Length} chars", normalized.Length);

            return Task.FromResult(normalized);
        }

        public Task<string> NormalizeExampleAsync(string example, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(example))
                return Task.FromResult(string.Empty);

            var normalized = example.Trim();
            if (!normalized.EndsWith(".") && !normalized.EndsWith("!") && !normalized.EndsWith("?"))
                normalized += ".";

            logger.LogDebug("Normalized example: {Length} chars", normalized.Length);

            return Task.FromResult(normalized);
        }
    }
}