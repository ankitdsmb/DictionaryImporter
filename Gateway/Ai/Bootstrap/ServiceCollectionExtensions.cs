using DictionaryImporter.Gateway.Ai.Abstractions;
using DictionaryImporter.Gateway.Ai.Configuration;
using DictionaryImporter.Gateway.Ai.Core;
using DictionaryImporter.Gateway.Ai.Merging;
using DictionaryImporter.Gateway.Ai.Providers;
using DictionaryImporter.Gateway.Ai.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace DictionaryImporter.Gateway.Ai.Bootstrap
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDictionaryImporterAiGateway(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<AiGatewayOptions>(configuration.GetSection("AiGateway"));

            services.AddHttpClient("DictionaryImporter.AiGateway");

            services.AddSingleton<IAiProviderSelector, DefaultProviderSelector>();
            services.AddSingleton<IAiResultMerger, SimpleTextMerger>();

            // ✅ FIX: Correct implementation type
            services.AddScoped<IAiGateway, AiGateway>();

            // Register provider clients from configuration dynamically
            services.AddScoped<IEnumerable<IAiProviderClient>>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<AiGatewayOptions>>().Value;
                var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
                var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();

                var list = new List<IAiProviderClient>();

                foreach (var p in opts.Providers)
                {
                    var logger = loggerFactory.CreateLogger<GenericHttpAiProviderClient>();
                    list.Add(new GenericHttpAiProviderClient(p, httpFactory, logger));
                }

                return list;
            });

            return services;
        }
    }
}
