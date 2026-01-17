namespace DictionaryImporter.Core.Graph
{
    public interface IGraphValidator
    {
        Task ValidateAsync(string sourceCode, CancellationToken ct);
    }
}