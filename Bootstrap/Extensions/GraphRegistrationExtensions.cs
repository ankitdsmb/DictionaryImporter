namespace DictionaryImporter.Bootstrap.Extensions;

internal static class GraphRegistrationExtensions
{
    public static IServiceCollection AddGraph(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(sp =>
            new DictionaryConceptBuilder(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryConceptBuilder>>()));

        services.AddSingleton(sp =>
            new DictionaryConceptConfidenceCalculator(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryConceptConfidenceCalculator>>()));

        services.AddSingleton(sp =>
            new DictionaryConceptMerger(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryConceptMerger>>()));

        services.AddSingleton<IGraphBuilder>(sp =>
            new DictionaryGraphBuilder(
                connectionString,
                sp.GetRequiredService<DictionaryConceptBuilder>(),
                sp.GetRequiredService<DictionaryConceptConfidenceCalculator>(),
                sp.GetRequiredService<DictionaryConceptMerger>(),
                sp.GetRequiredService<ILogger<DictionaryGraphBuilder>>()));

        services.AddSingleton(sp =>
            new DictionaryGraphNodeBuilder(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryGraphNodeBuilder>>()));


        services.AddSingleton(sp =>
            new DictionaryGraphValidator(
                connectionString,
                sp.GetRequiredService<ILogger<DictionaryGraphValidator>>()));

        return services;
    }
}