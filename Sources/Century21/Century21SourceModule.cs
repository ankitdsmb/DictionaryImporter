using DictionaryImporter.Core.Orchestration.Engine;
using DictionaryImporter.Core.Orchestration.Sources;
using DictionaryImporter.Infrastructure.Source;
using DictionaryImporter.Sources.Century21.Parsing;

namespace DictionaryImporter.Sources.Century21;

public sealed class Century21SourceModule : IDictionarySourceModule
{
    public string SourceCode => "CENTURY21";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // MUST HAVE THESE:
        services.AddSingleton<IDataExtractor<Century21RawEntry>, Century21Extractor>();
        services.AddSingleton<IDataTransformer<Century21RawEntry>, Century21Transformer>(); // ← CRITICAL
        services.AddSingleton<IDictionaryDefinitionParser, Century21DefinitionParser>();
        services.AddSingleton<IDictionaryEntryValidator, Century21EntryValidator>();
        services.AddSingleton<ImportEngineFactory<Century21RawEntry>>();
    }

    public ImportSourceDefinition BuildSource(IConfiguration config)
    {
        var filePath = config["Sources:Century21:FilePath"]
                       ?? throw new InvalidOperationException("Century21 file path not configured");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Century21 source file not found: {filePath}", filePath);

        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "21st Century Dictionary (Century21)",
            OpenStream = () => File.OpenRead(filePath),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }
}