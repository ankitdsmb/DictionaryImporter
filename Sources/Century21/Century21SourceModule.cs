using DictionaryImporter.Sources.Century21.Parsing;

namespace DictionaryImporter.Sources.Century21
{
    public sealed class Century21SourceModule : IDictionarySourceModule
    {
        public string SourceCode => "CENTURY21";

        public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddSingleton<IDataExtractor<Century21RawEntry>, Country21Extractor>();
            services.AddSingleton<IDataTransformer<Century21RawEntry>, Century21Transformer>();
            services.AddSingleton<IDictionaryDefinitionParser, Century21DefinitionParser>();
            services.AddSingleton<IDictionaryEntryValidator, Country21EntryValidator>();
            services.AddSingleton<ImportEngineFactory<Century21RawEntry>>();
        }

        public ImportSourceDefinition BuildSource(IConfiguration config)
        {
            var filePath = config["Sources:Century21:FilePath"]
                           ?? throw new InvalidOperationException("Century21 file path not configured");
            return new ImportSourceDefinition
            {
                SourceCode = SourceCode,
                SourceName = "21st Century Dictionary (Century21)",
                OpenStream = () => File.OpenRead(filePath),
                GraphRebuildMode = GraphRebuildMode.Rebuild
            };
        }
    }
}