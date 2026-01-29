namespace DictionaryImporter.Core.Orchestration.Sources;

public sealed class ImportSourceDefinition
{
    public string SourceCode { get; init; } = default!;
    public string SourceName { get; init; } = default!;
    public Func<Stream> OpenStream { get; init; } = default!;

    public GraphRebuildMode GraphRebuildMode { get; init; }
}