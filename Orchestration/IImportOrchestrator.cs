namespace DictionaryImporter.Orchestration;

public interface IImportOrchestrator
{
    Task RunAsync(
        IEnumerable<ImportSourceDefinition> sources,
        CancellationToken cancellationToken);
}