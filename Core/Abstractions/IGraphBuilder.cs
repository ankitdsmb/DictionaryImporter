namespace DictionaryImporter.Core.Abstractions
{
    public interface IGraphBuilder
    {
        Task BuildAsync(string sourceCode, CancellationToken ct);
    }
}