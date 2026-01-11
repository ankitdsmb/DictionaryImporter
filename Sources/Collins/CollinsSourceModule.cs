using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Core.Sources;
using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Orchestration;
using DictionaryImporter.Sources.Collins.Models;
using DictionaryImporter.Sources.Collins.Parsing;
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
            services.AddSingleton<
                IDataExtractor<CollinsRawEntry>,
                CollinsExtractor>();

            services.AddSingleton<
                IDataTransformer<CollinsRawEntry>,
                CollinsTransformer>();

            services.AddSingleton<
                IDictionaryDefinitionParser,
                CollinsDefinitionParser>();
        }

        public ImportSourceDefinition BuildSource(IConfiguration config)
        {
            var filePath = config["Sources:Collins:FilePath"]
                ?? throw new InvalidOperationException("Collins file path not configured");

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