// File: Infrastructure/Persistence/INonEnglishTextStorage.cs
namespace DictionaryImporter.Infrastructure.Persistence
{
    public interface INonEnglishTextStorage
    {
        Task<long?> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct);

        Task<string?> GetNonEnglishTextAsync(long nonEnglishTextId, CancellationToken ct);
    }
}
