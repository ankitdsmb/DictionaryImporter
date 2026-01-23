namespace DictionaryImporter.Core.Persistence
{
    public interface IDictionaryEntryVariantWriter
    {
        Task WriteAsync(long dictionaryEntryId, string variantText, string variantType, string sourceCode, CancellationToken ct);
    }
}