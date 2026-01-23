namespace DictionaryImporter.Core.Abstractions
{
    public interface IDictionaryPostProcessor
    {
        Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct);
    }
}