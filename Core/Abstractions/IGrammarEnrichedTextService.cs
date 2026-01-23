namespace DictionaryImporter.Core.Abstractions;

public interface IGrammarEnrichedTextService
{
    Task<string> NormalizeDefinitionAsync(string definition, CancellationToken ct);
    Task<string> NormalizeExampleAsync(string example, CancellationToken ct);
}