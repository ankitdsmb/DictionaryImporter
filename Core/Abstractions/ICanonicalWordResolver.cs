namespace DictionaryImporter.Core.Abstractions;

public interface ICanonicalWordResolver
{
    Task ResolveAsync(
        string sourceCode,
        CancellationToken ct);
}