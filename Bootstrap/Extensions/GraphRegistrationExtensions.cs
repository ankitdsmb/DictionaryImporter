namespace DictionaryImporter.Bootstrap.Extensions;

internal static class GraphRegistrationExtensions
{
    public static IServiceCollection AddGraph(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(sp =>
            new DictionaryGraphNodeBuilder(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryGraphNodeBuilder>>()));

        services.AddSingleton(sp =>
            new DictionaryGraphBuilder(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryGraphBuilder>>()));

        services.AddSingleton(sp =>
            new DictionaryGraphValidator(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryGraphValidator>>()));

        return services;
    }
}