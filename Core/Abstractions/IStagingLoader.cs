namespace DictionaryImporter.Core.Abstractions;

public interface IStagingLoader
{
    Task LoadAsync(
        IEnumerable<DictionaryEntryStaging> entries,
        CancellationToken cancellationToken);
}