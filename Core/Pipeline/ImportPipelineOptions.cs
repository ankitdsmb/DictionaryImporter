namespace DictionaryImporter.Core.Pipeline
{
    public sealed class ImportPipelineOptions
    {
        public List<string> DefaultSteps { get; set; } = new();

        public Dictionary<string, SourcePipelineOptions> Sources { get; set; } = new();
    }
}