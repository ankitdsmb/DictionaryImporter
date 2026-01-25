using DictionaryImporter.Infrastructure.Persistence.Mapping;

namespace DictionaryImporter.Infrastructure;

public sealed class StagingDataLoaderAdapter(IStagingLoader stagingLoader) : IDataLoader
{
    public Task LoadAsync(
        IEnumerable<DictionaryEntry> entries,
        CancellationToken cancellationToken)
    {
        var stagingEntries = entries.Select(StagingMapper.Map);
        return stagingLoader.LoadAsync(stagingEntries, cancellationToken);
    }
}