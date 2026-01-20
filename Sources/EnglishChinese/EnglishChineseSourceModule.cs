using System;
using System.IO;
using DictionaryImporter.Sources.EnglishChinese.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources.EnglishChinese
{
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
            // FIX: Register ALL required services
            services.AddSingleton<IDataExtractor<EnglishChineseRawEntry>, EnglishChineseExtractor>();
            services.AddSingleton<IDataTransformer<EnglishChineseRawEntry>, EnglishChineseTransformer>();
            services.AddSingleton<IDictionaryDefinitionParser, EnglishChineseDefinitionParser>();
            services.AddSingleton<IDictionaryEntryValidator, EnglishChineseEntryValidator>();

            // FIX: Register the factory
            services.AddSingleton<ImportEngineFactory<EnglishChineseRawEntry>>();
        }
    }
}