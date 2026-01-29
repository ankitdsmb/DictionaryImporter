namespace DictionaryImporter.Infrastructure.Persistence;

public interface IDictionaryImportControl
{
    Task<bool> MarkSourceCompletedAsync(
        string sourceCode,
        CancellationToken ct);

    Task TryFinalizeAsync(string sourceCode, CancellationToken ct);
}