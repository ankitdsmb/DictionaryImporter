namespace DictionaryImporter.Core.Pipeline;

public sealed class ImportPipelineContext(ImportSourceDefinition source, CancellationToken cancellationToken)
{
    public ImportSourceDefinition Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    public string SourceCode => Source.SourceCode;
    public CancellationToken CancellationToken { get; } = cancellationToken;
}