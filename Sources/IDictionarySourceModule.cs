using DictionaryImporter.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DictionaryImporter.Sources;

public interface IDictionarySourceModule
{
    string SourceCode { get; }

    ImportSourceDefinition BuildSource(IConfiguration config);

    void RegisterServices(
        IServiceCollection services,
        IConfiguration config);
}