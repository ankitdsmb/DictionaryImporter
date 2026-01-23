namespace DictionaryImporter.Core.Abstractions
{
    public interface IImportEngine
    {
        Task ImportAsync(Stream stream, CancellationToken ct);
    }
}