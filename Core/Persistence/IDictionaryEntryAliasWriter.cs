namespace DictionaryImporter.Core.Persistence
{
    public interface IDictionaryEntryAliasWriter
    {
        Task WriteAsync(long parsedDefinitionId, string aliasText, CancellationToken ct);
    }
}