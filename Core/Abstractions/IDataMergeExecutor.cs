namespace DictionaryImporter.Core.Abstractions
{
    public interface IDataMergeExecutor
    {
        Task ExecuteAsync(
            string sourceCode,
            CancellationToken cancellationToken);
    }
}