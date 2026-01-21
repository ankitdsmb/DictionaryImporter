namespace DictionaryImporter.Core.Persistence
{
    public interface IDictionaryEntryCrossReferenceWriter
    {
        Task WriteAsync(long sourceParsedId, CrossReference crossReference, CancellationToken ct);
    }
}