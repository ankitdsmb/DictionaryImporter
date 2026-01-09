using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Sources;
using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Orchestration;
using DictionaryImporter.Sources.Gutenberg;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources.GutenbergWebster
{
    public sealed class GutenbergWebsterSourceModule
        : IDictionarySourceModule
    {
        public string SourceCode => "GUT_WEBSTER";

        public void RegisterServices(
            IServiceCollection services,
            IConfiguration config)
        {
            services.AddSingleton<
                IDataExtractor<GutenbergRawEntry>,
                GutenbergWebsterExtractor>();

            services.AddSingleton<
                IDataTransformer<GutenbergRawEntry>,
                GutenbergWebsterTransformer>();
        }

        public ImportSourceDefinition BuildSource(
            IConfiguration config)
        {
            var filePath =
                config["Sources:GutenbergWebster:FilePath"]
                ?? throw new InvalidOperationException(
                    "GutenbergWebster file path not configured");

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
