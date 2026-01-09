namespace DictionaryImporter.Core.Pipeline
{
    public interface IImportEngine
    {
        Task ImportAsync(Stream stream, CancellationToken ct);
    }
}