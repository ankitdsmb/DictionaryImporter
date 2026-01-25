namespace DictionaryImporter.Core.Abstractions
{
    public interface IDictionaryEntryCrossReferenceWriter
    {
        Task WriteAsync(long sourceParsedId, CrossReference crossReference, string sourceCode, CancellationToken ct);
    }
}