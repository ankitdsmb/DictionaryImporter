namespace DictionaryImporter.Infrastructure.Source
{
    public interface IDictionarySourceModule
    {
        string SourceCode { get; }

        ImportSourceDefinition BuildSource(IConfiguration config);

        void RegisterServices(
            IServiceCollection services,
            IConfiguration config);
    }
}