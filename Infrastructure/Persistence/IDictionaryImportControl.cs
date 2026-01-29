namespace DictionaryImporter.Infrastructure.Persistence;

public interface IDictionaryImportControl
{
    Task<bool> MarkSourceCompletedAsync(
        string sourceCode,
        CancellationToken ct);

    Task TryFinalizeAsync(CancellationToken ct);
}