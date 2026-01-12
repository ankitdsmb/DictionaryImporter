using Microsoft.Extensions.Configuration;

namespace DictionaryImporter.Bootstrap;

public static class BootstrapConfiguration
{
    public static IConfiguration Build()
    {
        return new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", false)
            .AddEnvironmentVariables()
            .Build();
    }
}