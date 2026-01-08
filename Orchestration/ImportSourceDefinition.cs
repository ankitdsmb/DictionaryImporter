namespace DictionaryImporter.Orchestration
{
    public sealed class ImportSourceDefinition
    {
        public string SourceCode { get; init; } = null!;
        public string SourceName { get; init; } = null!;
        public Func<Stream> OpenStream { get; init; } = null!;
    }
}