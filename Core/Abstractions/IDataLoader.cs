using DictionaryImporter.Domain.Models;

namespace DictionaryImporter.Core.Abstractions
{
    public interface IDataLoader
    {
        Task LoadAsync(
            IEnumerable<DictionaryEntry> entries,
            CancellationToken cancellationToken);
    }
}
