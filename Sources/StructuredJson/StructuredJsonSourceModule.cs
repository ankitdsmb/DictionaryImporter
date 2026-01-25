using System;
using System.IO;
using DictionaryImporter.Infrastructure.Source;
using DictionaryImporter.Sources.StructuredJson.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources.StructuredJson;

public sealed class StructuredJsonSourceModule : IDictionarySourceModule
{
    public string SourceCode => "STRUCT_JSON";

    public void RegisterServices(
        IServiceCollection services,
        IConfiguration config)
    {
        // FIX: Register ALL required services
        services.AddSingleton<IDataExtractor<StructuredJsonRawEntry>, StructuredJsonExtractor>();
        services.AddSingleton<IDataTransformer<StructuredJsonRawEntry>, StructuredJsonTransformer>();
        services.AddSingleton<IDictionaryDefinitionParser, StructuredJsonDefinitionParser>();

        // FIX: Register the factory
        services.AddSingleton<ImportEngineFactory<StructuredJsonRawEntry>>();
    }

    public ImportSourceDefinition BuildSource(
        IConfiguration config)
    {
        var filePath = config["Sources:StructuredJson:FilePath"]
                       ?? throw new InvalidOperationException("StructuredJson file path not configured");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"StructuredJson source file not found: {filePath}", filePath);

        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "Structured English Dictionary (JSON)",
            OpenStream = () => File.OpenRead(filePath),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }
}