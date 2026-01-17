namespace DictionaryImporter.Core.Pipeline
{
    public interface IImportPipelineStep
    {
        string Name { get; }

        Task ExecuteAsync(ImportPipelineContext context);
    }
}