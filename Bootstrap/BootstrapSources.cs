using DictionaryImporter.Infrastructure.Source;

namespace DictionaryImporter.Bootstrap;

public static class BootstrapSources
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var sources = SourceRegistry.CreateSources();

        foreach (var module in sources)
            module.RegisterServices(services, configuration);

        services.AddSingleton<IEnumerable<IDictionarySourceModule>>(sources);
    }
}