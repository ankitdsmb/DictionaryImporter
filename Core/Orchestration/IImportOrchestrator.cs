using DictionaryImporter.Core.Orchestration.Models;
using DictionaryImporter.Core.Orchestration.Sources;

namespace DictionaryImporter.Core.Orchestration;

public interface IImportOrchestrator
{
    Task RunAsync(
        IEnumerable<ImportSourceDefinition> sources,
        PipelineMode mode,
        CancellationToken cancellationToken);
}