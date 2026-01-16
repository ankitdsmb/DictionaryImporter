namespace DictionaryImporter.Sources.Collins;

public sealed class CollinsSourceModule : IDictionarySourceModule
{
    public string SourceCode => "ENG_COLLINS";

    public void RegisterServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<
            IDataExtractor<CollinsRawEntry>,
            CollinsExtractor>();

        services.AddSingleton<
            IDataTransformer<CollinsRawEntry>,
            CollinsTransformer>();

        services.AddSingleton<
            IDictionaryDefinitionParser,
            CollinsDefinitionParser>();
    }

    public ImportSourceDefinition BuildSource(IConfiguration config)
    {
        var filePath = config["Sources:Collins:FilePath"]
                       ?? throw new InvalidOperationException("Collins file path not configured");

        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "Collins Bilingual Dictionary",
            OpenStream = () => File.OpenRead(filePath),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }
}