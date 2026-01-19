// In KaikkiSourceModule.cs - Ensure all extractors are registered

using DictionaryImporter.Sources.Kaikki;
using DictionaryImporter.Sources.Kaikki.Parsing;

public sealed class KaikkiSourceModule : IDictionarySourceModule
{
    public string SourceCode => "KAIKKI";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDataExtractor<KaikkiRawEntry>, KaikkiExtractor>();
        services.AddSingleton<IDataTransformer<KaikkiRawEntry>, KaikkiTransformer>();
        services.AddSingleton<IDictionaryDefinitionParser, KaikkiDefinitionParser>();

        // Register Kaikki-specific extractors
        services.AddSingleton<IEtymologyExtractor, KaikkiEtymologyExtractor>();
        services.AddSingleton<IExampleExtractor, KaikkiExampleExtractor>();
        services.AddSingleton<ISynonymExtractor, KaikkiSynonymExtractor>();

        // Register the factory
        services.AddSingleton<ImportEngineFactory<KaikkiRawEntry>>();
    }

    public ImportSourceDefinition BuildSource(IConfiguration config)
    {
        var filePath = config["Sources:Kaikki:FilePath"]
                       ?? throw new InvalidOperationException("Kaikki file path not configured");

        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "Kaikki Wiktionary Data",
            OpenStream = () => File.OpenRead(filePath),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }
}