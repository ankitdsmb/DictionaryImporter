using DictionaryImporter.Core.Orchestration.Models;

namespace DictionaryImporter.Core.Orchestration.Pipeline;

public sealed class ImportPipelineOptions
{
    public List<string> DefaultSteps { get; set; } = new();

    public Dictionary<string, SourcePipelineOptions> Sources { get; set; } = new();
}