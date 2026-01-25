namespace DictionaryImporter.Core.Abstractions;

public interface IParsedDefinitionWriter
{
    Task<long> WriteAsync(
        long dictionaryEntryId,
        ParsedDefinition parsed,
        CancellationToken ct);
}