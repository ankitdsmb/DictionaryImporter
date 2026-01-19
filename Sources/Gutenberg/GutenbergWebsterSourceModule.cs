using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergWebsterSourceModule : IDictionarySourceModule
    {
        public string SourceCode => "GUT_WEBSTER";

        public void RegisterServices(IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IDictionaryDefinitionParser, GutenbergDefinitionParser>();
            services.AddSingleton<IDataExtractor<GutenbergRawEntry>, GutenbergWebsterExtractor>();
            services.AddSingleton<IDataTransformer<GutenbergRawEntry>, GutenbergWebsterTransformer>();
        }

        public ImportSourceDefinition BuildSource(IConfiguration config)
        {
            var filePath =
                config["Sources:GutenbergWebster:FilePath"]
                ?? throw new InvalidOperationException("GutenbergWebster file path not configured");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"GutenbergWebster source file not found: {filePath}", filePath);

            return new ImportSourceDefinition
            {
                SourceCode = SourceCode,
                SourceName = "Project Gutenberg – Webster",
                OpenStream = () => File.OpenRead(filePath),
                GraphRebuildMode = GraphRebuildMode.Append
            };
        }
    }
}