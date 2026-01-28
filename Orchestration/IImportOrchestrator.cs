namespace DictionaryImporter.Orchestration;

public interface IImportOrchestrator
{
    Task RunAsync(
        IEnumerable<ImportSourceDefinition> sources,
        PipelineMode mode,
        CancellationToken cancellationToken);
}