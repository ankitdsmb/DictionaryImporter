namespace DictionaryImporter.Core.Canonical
{
    public interface ICanonicalWordResolver
    {
        Task ResolveAsync(
            string sourceCode,
            CancellationToken ct);
    }
}