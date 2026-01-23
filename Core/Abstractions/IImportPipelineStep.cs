namespace DictionaryImporter.Core.Abstractions
{
    public interface IImportPipelineStep
    {
        string Name { get; }

        Task ExecuteAsync(ImportPipelineContext context);
    }
}