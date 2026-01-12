namespace DictionaryImporter.Core.Persistence;

public interface IParsedDefinitionWriter
{
    Task<long> WriteAsync(
        long dictionaryEntryId,
        ParsedDefinition parsed,
        CancellationToken ct);
}