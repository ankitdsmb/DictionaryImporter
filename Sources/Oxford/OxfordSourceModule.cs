using DictionaryImporter.Sources.Oxford.Parsing;

namespace DictionaryImporter.Sources.Oxford
{
    public sealed class OxfordSourceModule : IDictionarySourceModule
    {
        public string SourceCode => "ENG_OXFORD";

        public void RegisterServices(
            IServiceCollection services,
            IConfiguration configuration)
        {
            // FIX: Register ALL required services
            services.AddSingleton<IDataExtractor<OxfordRawEntry>, OxfordExtractor>();
            services.AddSingleton<IDataTransformer<OxfordRawEntry>, OxfordTransformer>();
            services.AddSingleton<IDictionaryDefinitionParser, OxfordDefinitionParser>();
            services.AddSingleton<IDictionaryEntryValidator, OxfordEntryValidator>();

            // FIX: Register the factory
            services.AddSingleton<ImportEngineFactory<OxfordRawEntry>>();
        }

        public ImportSourceDefinition BuildSource(IConfiguration config)
        {
            var filePath = config["Sources:Oxford:FilePath"]
                           ?? throw new InvalidOperationException("Oxford file path not configured");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Oxford source file not found: {filePath}", filePath);

            return new ImportSourceDefinition
            {
                SourceCode = SourceCode,
                SourceName = "Oxford English-Chinese Dictionary",
                OpenStream = () => File.OpenRead(filePath),
                GraphRebuildMode = GraphRebuildMode.Rebuild
            };
        }
    }
}