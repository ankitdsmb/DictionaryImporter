using DictionaryImporter.Infrastructure.Persistence.Mapping;

namespace DictionaryImporter.Infrastructure;

public sealed class StagingDataLoaderAdapter : IDataLoader
{
    private readonly IStagingLoader _stagingLoader;

    public StagingDataLoaderAdapter(IStagingLoader stagingLoader)
    {
        _stagingLoader = stagingLoader;
    }

    public Task LoadAsync(
        IEnumerable<DictionaryEntry> entries,
        CancellationToken cancellationToken)
    {
        var stagingEntries = entries.Select(StagingMapper.Map);
        return _stagingLoader.LoadAsync(stagingEntries, cancellationToken);
    }
}