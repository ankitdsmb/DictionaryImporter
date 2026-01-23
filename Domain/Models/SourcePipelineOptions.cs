namespace DictionaryImporter.Domain.Models
{
    public sealed class SourcePipelineOptions
    {
        public List<string> Steps { get; set; } = new();
    }
}