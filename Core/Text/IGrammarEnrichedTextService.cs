namespace DictionaryImporter.Core.Text;

public interface IGrammarEnrichedTextService
{
    Task<string> NormalizeDefinitionAsync(string definition, CancellationToken ct);
    Task<string> NormalizeExampleAsync(string example, CancellationToken ct);
}