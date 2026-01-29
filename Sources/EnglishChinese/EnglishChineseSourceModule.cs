using DictionaryImporter.Core.Orchestration.Engine;
using DictionaryImporter.Core.Orchestration.Sources;
using DictionaryImporter.Infrastructure.Source;
using DictionaryImporter.Sources.EnglishChinese.Parsing;

namespace DictionaryImporter.Sources.EnglishChinese;

public sealed class EnglishChineseSourceModule : IDictionarySourceModule
{
    public string SourceCode => "ENG_CHN";

    public ImportSourceDefinition BuildSource(IConfiguration configuration)
    {
        var filePath = configuration["Sources:EnglishChinese:FilePath"]
                       ?? throw new InvalidOperationException("EnglishChinese source file path not configured");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"EnglishChinese source file not found: {filePath}", filePath);

        return new ImportSourceDefinition
        {
            SourceCode = SourceCode,
            SourceName = "English–Chinese Dictionary",
            OpenStream = () => File.OpenRead(filePath),
            GraphRebuildMode = GraphRebuildMode.Rebuild
        };
    }

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // ✅ Register ALL required services including validator
        services.AddSingleton<IDataExtractor<EnglishChineseRawEntry>, EnglishChineseExtractor>();
        services.AddSingleton<IDataTransformer<EnglishChineseRawEntry>, EnglishChineseTransformer>();
        services.AddSingleton<IDictionaryDefinitionParser, EnglishChineseParser>();
        services.AddSingleton<IDictionaryEntryValidator, EnglishChineseEntryValidator>();
        services.AddSingleton<ImportEngineFactory<EnglishChineseRawEntry>>();
    }
}