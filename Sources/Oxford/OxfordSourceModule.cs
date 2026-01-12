using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Orchestration;
using DictionaryImporter.Sources.Gutenberg.Parsing;
using DictionaryImporter.Sources.Oxford.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources.Oxford;

public sealed class OxfordSourceModule : IDictionarySourceModule
{
    public string SourceCode => "ENG_OXFORD";

    public void RegisterServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<
            IDataExtractor<OxfordRawEntry>,
            OxfordExtractor>();

        services.AddSingleton<
            IDataTransformer<OxfordRawEntry>,
            OxfordTransformer>();

        services.AddSingleton<
            IDictionaryDefinitionParser,
            OxfordDefinitionParser>();

        services.AddSingleton<
            IDictionaryEntryValidator,
            OxfordEntryValidator>();
    }

    public ImportSourceDefinition BuildSource(IConfiguration config)
    {
        var filePath = config["Sources:Oxford:FilePath"]
                       ?? throw new InvalidOperationException("Oxford file path not configured");

        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "Oxford English-Chinese Dictionary",
            OpenStream = () => File.OpenRead(filePath),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }
}