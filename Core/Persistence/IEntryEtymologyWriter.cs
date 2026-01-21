namespace DictionaryImporter.Core.Persistence
{
    public interface IEntryEtymologyWriter
    {
        Task WriteAsync(DictionaryEntryEtymology etymology, CancellationToken ct);
    }
}