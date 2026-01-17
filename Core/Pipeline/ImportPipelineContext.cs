namespace DictionaryImporter.Core.Pipeline
{
    public sealed class ImportPipelineContext
    {
        public ImportPipelineContext(ImportSourceDefinition source, CancellationToken cancellationToken)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            CancellationToken = cancellationToken;
        }

        public ImportSourceDefinition Source { get; }
        public string SourceCode => Source.SourceCode;
        public CancellationToken CancellationToken { get; }
    }
}