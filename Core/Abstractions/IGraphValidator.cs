namespace DictionaryImporter.Core.Abstractions;

public interface IGraphValidator
{
    Task ValidateAsync(string sourceCode, CancellationToken ct);
}