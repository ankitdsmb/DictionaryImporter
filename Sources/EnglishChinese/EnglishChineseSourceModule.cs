using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Orchestration;
using DictionaryImporter.Sources.EnglishChinese.Models;
using DictionaryImporter.Sources.EnglishChinese.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseSourceModule : IDictionarySourceModule
{
    public string SourceCode => "ENG_CHN";

    public ImportSourceDefinition BuildSource(IConfiguration configuration)
    {
        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "English–Chinese Dictionary",
            OpenStream = () => File.OpenRead(configuration["Sources:EnglishChinese:FilePath"] ??
                                             throw new InvalidOperationException(
                                                 "EnglishChinese source file path not configured")),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDataExtractor<EnglishChineseRawEntry>, EnglishChineseExtractor>();
        services.AddSingleton<IDataTransformer<EnglishChineseRawEntry>, EnglishChineseTransformer>();
        services.AddSingleton<IDictionaryDefinitionParser, EnglishChineseDefinitionParser>();
        services.AddSingleton<ImportEngineFactory<EnglishChineseRawEntry>>();
    }
}