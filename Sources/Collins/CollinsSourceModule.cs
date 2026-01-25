using System;
using System.IO;
using DictionaryImporter.Infrastructure.Source;
using DictionaryImporter.Sources.Collins.parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources.Collins
{
    public sealed class CollinsSourceModule : IDictionarySourceModule
    {
        public string SourceCode => "ENG_COLLINS";

        public void RegisterServices(
            IServiceCollection services,
            IConfiguration configuration)
        {
            // FIX: Register ALL required services
            services.AddSingleton<IDataExtractor<CollinsRawEntry>, CollinsExtractor>();
            services.AddSingleton<IDataTransformer<CollinsRawEntry>, CollinsTransformer>();
            services.AddSingleton<IDictionaryDefinitionParser, CollinsDefinitionParser>();
            services.AddSingleton<IDictionaryEntryValidator, CollinsEntryValidator>();

            // FIX: Register the factory
            services.AddSingleton<ImportEngineFactory<CollinsRawEntry>>();
        }

        public ImportSourceDefinition BuildSource(IConfiguration config)
        {
            var filePath = config["Sources:Collins:FilePath"]
                           ?? throw new InvalidOperationException("Collins file path not configured");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Collins source file not found: {filePath}", filePath);

            return new ImportSourceDefinition
            {
                SourceCode = SourceCode,
                SourceName = "Collins Bilingual Dictionary",
                OpenStream = () => File.OpenRead(filePath),
                GraphRebuildMode = GraphRebuildMode.Rebuild
            };
        }
    }
}