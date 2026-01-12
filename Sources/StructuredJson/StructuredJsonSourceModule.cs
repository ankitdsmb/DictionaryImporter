namespace DictionaryImporter.Sources.StructuredJson;

public sealed class StructuredJsonSourceModule
    : IDictionarySourceModule
{
    public string SourceCode => "STRUCT_JSON";

    public void RegisterServices(
        IServiceCollection services,
        IConfiguration config)
    {
        services.AddSingleton<
            IDataExtractor<StructuredJsonRawEntry>,
            StructuredJsonExtractor>();

        services.AddSingleton<
            IDataTransformer<StructuredJsonRawEntry>,
            StructuredJsonTransformer>();
    }

    public ImportSourceDefinition BuildSource(
        IConfiguration config)
    {
        var filePath =
            config["Sources:StructuredJson:FilePath"]
            ?? throw new InvalidOperationException(
                "StructuredJson file path not configured");

        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "Structured English Dictionary (JSON)",
            OpenStream = () => File.OpenRead(filePath),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }
}