namespace DictionaryImporter.Core.PostProcessing
{
    public interface IDictionaryPostProcessor
    {
        Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct);
    }
}
