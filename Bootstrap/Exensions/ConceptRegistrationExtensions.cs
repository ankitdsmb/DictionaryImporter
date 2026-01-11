using DictionaryImporter.Infrastructure.Graph;
using DictionaryImporter.Infrastructure.PostProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Bootstrap.Exensions
{
    internal static class ConceptRegistrationExtensions
    {
        public static IServiceCollection AddConcepts(
            this IServiceCollection services,
            string connectionString)
        {
            services.AddSingleton(sp =>
                new DictionaryConceptBuilder(
                    connectionString,
                    sp.GetRequiredService<ILogger<DictionaryConceptBuilder>>()));

            services.AddSingleton(sp =>
                new DictionaryConceptMerger(
                    connectionString,
                    sp.GetRequiredService<ILogger<DictionaryConceptMerger>>()));

            services.AddSingleton(sp =>
                new DictionaryConceptConfidenceCalculator(
                    connectionString,
                    sp.GetRequiredService<ILogger<DictionaryConceptConfidenceCalculator>>()));

            services.AddSingleton(sp =>
                new DictionaryGraphRankCalculator(
                    connectionString,
                    sp.GetRequiredService<ILogger<DictionaryGraphRankCalculator>>()));

            return services;
        }
    }
}
