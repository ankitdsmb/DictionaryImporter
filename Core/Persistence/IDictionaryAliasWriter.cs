namespace DictionaryImporter.Core.Persistence
{
    public interface IDictionaryAliasWriter
    {
        Task WriteAsync(
            long parsedDefinitionId,
            string alias,
            CancellationToken ct);
    }
}