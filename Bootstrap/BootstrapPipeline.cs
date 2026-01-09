using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Validation;
using DictionaryImporter.Orchestration;
using DictionaryImporter.Sources.Gutenberg;
using DictionaryImporter.Sources.StructuredJson;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Bootstrap
{
    public static class BootstrapPipeline
    {
        public static void Register(
            IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<ImportEngineFactory<GutenbergRawEntry>>();
            services.AddSingleton<ImportEngineFactory<StructuredJsonRawEntry>>();

            services.AddSingleton<Func<IDictionaryEntryValidator>>(sp =>
                () => sp.GetRequiredService<IDictionaryEntryValidator>());

            services.AddSingleton<Func<IDataMergeExecutor>>(sp =>
                () => sp.GetRequiredService<IDataMergeExecutor>());

            services.AddSingleton<Func<ImportEngineFactory<GutenbergRawEntry>>>(sp =>
                () => sp.GetRequiredService<ImportEngineFactory<GutenbergRawEntry>>());

            services.AddSingleton<Func<ImportEngineFactory<StructuredJsonRawEntry>>>(sp =>
                () => sp.GetRequiredService<ImportEngineFactory<StructuredJsonRawEntry>>());

            services.AddSingleton<IImportEngineRegistry, ImportEngineRegistry>();
            services.AddSingleton<ImportOrchestrator>();
        }

        public static PipelineMode ResolvePipelineMode(IConfiguration configuration)
        {
            return configuration["Pipeline:Mode"] == "ImportOnly"
                ? PipelineMode.ImportOnly
                : PipelineMode.Full;
        }
    }
}
