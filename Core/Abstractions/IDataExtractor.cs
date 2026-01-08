namespace DictionaryImporter.Core.Abstractions
{
    public interface IDataExtractor<TRaw>
    {
        IAsyncEnumerable<TRaw> ExtractAsync(
            Stream source,
            CancellationToken cancellationToken);
    }
}
