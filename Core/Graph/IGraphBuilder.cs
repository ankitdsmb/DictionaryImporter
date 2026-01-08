namespace DictionaryImporter.Core.Graph
{
    public interface IGraphBuilder
    {
        Task BuildAsync(string sourceCode, CancellationToken ct);
    }
}
