namespace DictionaryImporter.Core.Abstractions;

public interface IDataLoader
{
    Task LoadAsync(
        IEnumerable<DictionaryEntry> entries,
        CancellationToken cancellationToken);
}