using DictionaryImporter.Infrastructure.Graph;
using Microsoft.Extensions.DependencyInjection;

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

        // IMPORTANT:
        // Some pipeline/orchestrator code requests DictionaryGraphBuilder directly (concrete),
        // while some requests IGraphBuilder (interface).
        // This alias guarantees BOTH work and resolve to the SAME singleton instance.
        services.AddSingleton<DictionaryGraphBuilder>(sp =>
            (DictionaryGraphBuilder)sp.GetRequiredService<IGraphBuilder>());

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