using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Contracts;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DictionaryImporter.AI.Core.Attributes;

namespace DictionaryImporter.AI.Infrastructure
{
    public class ProviderFactory : IProviderFactory, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AiOrchestrationConfiguration _config;
        private readonly ILogger<ProviderFactory> _logger;
        private readonly ConfigurationValidator _validator;

        private readonly Dictionary<string, Type> _providerTypes;
        private readonly Dictionary<string, ICompletionProvider> _providerCache;
        private readonly object _cacheLock = new object();

        private readonly List<IDisposable> _disposableProviders = new();

        private bool _disposed = false;

        public ProviderFactory(
            IServiceProvider serviceProvider,
            IOptions<AiOrchestrationConfiguration> config,
            ILogger<ProviderFactory> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _config = config.Value ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _validator = new ConfigurationValidator(
                _serviceProvider.GetRequiredService<ILogger<ConfigurationValidator>>());

            _providerTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            _providerCache = new Dictionary<string, ICompletionProvider>(StringComparer.OrdinalIgnoreCase);

            DiscoverProviderTypes();
            ValidateConfigurations();
        }

        private void DiscoverProviderTypes()
        {
            var providerAssembly = typeof(OpenRouterProvider).Assembly;
            var providerBaseType = typeof(ICompletionProvider);

            foreach (var type in providerAssembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract || !providerBaseType.IsAssignableFrom(type))
                    continue;

                var providerAttribute = type.GetCustomAttribute<ProviderAttribute>();
                if (providerAttribute == null)
                    continue;

                _providerTypes[providerAttribute.Name] = type;
                _logger.LogDebug("Discovered provider: {Provider} -> {Type}",
                    providerAttribute.Name, type.Name);
            }

            _logger.LogInformation("Discovered {Count} provider types", _providerTypes.Count);
        }

        private void ValidateConfigurations()
        {
            foreach (var providerName in _providerTypes.Keys)
            {
                if (!_config.Providers.ContainsKey(providerName))
                {
                    _logger.LogWarning("Configuration missing for provider: {Provider}", providerName);
                }
            }
        }

        public ICompletionProvider CreateProvider(string providerName)
        {
            if (string.IsNullOrEmpty(providerName))
                throw new ArgumentException("Provider name cannot be null or empty", nameof(providerName));

            lock (_cacheLock)
            {
                if (_providerCache.TryGetValue(providerName, out var cachedProvider))
                {
                    _logger.LogDebug("Returning cached provider: {Provider}", providerName);
                    return cachedProvider;
                }
            }

            if (!_providerTypes.TryGetValue(providerName, out var providerType))
                throw new ArgumentException($"Unknown provider: {providerName}", nameof(providerName));

            if (!_config.Providers.TryGetValue(providerName, out var providerConfig))
            {
                providerConfig = new ProviderConfiguration
                {
                    Name = providerName,
                    IsEnabled = false
                };
                _logger.LogWarning("Using default configuration for provider: {Provider}", providerName);
            }

            if (!providerConfig.IsEnabled)
                throw new InvalidOperationException($"Provider {providerName} is disabled");

            try
            {
                var provider = CreateProviderInstance(providerType, providerConfig);

                lock (_cacheLock)
                {
                    _providerCache[providerName] = provider;
                }

                _logger.LogInformation("Created provider instance: {Provider}", providerName);
                return provider;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create provider instance for {Provider}", providerName);
                throw new InvalidOperationException($"Failed to create provider {providerName}", ex);
            }
        }

        private ICompletionProvider CreateProviderInstance(Type providerType, ProviderConfiguration config)
        {
            var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(providerType.Name);

            var loggerType = typeof(ILogger<>).MakeGenericType(providerType);
            var logger = (ILogger)_serviceProvider.GetRequiredService(loggerType);
            var options = Options.Create(config);

            var quotaManager = _serviceProvider.GetService<IQuotaManager>();
            var auditLogger = _serviceProvider.GetService<IAuditLogger>();
            var responseCache = _serviceProvider.GetService<IResponseCache>();
            var metricsCollector = _serviceProvider.GetService<IPerformanceMetricsCollector>();
            var apiKeyManager = _serviceProvider.GetService<IApiKeyManager>();

            var constructor = providerType.GetConstructors().FirstOrDefault();
            if (constructor == null)
                throw new InvalidOperationException($"No constructor found for {providerType.Name}");

            var parameters = constructor.GetParameters();
            var arguments = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (param.ParameterType == typeof(HttpClient))
                    arguments[i] = httpClient;
                else if (param.ParameterType == loggerType)
                    arguments[i] = logger;
                else if (param.ParameterType == typeof(IOptions<ProviderConfiguration>))
                    arguments[i] = options;
                else if (param.ParameterType == typeof(IQuotaManager))
                    arguments[i] = quotaManager;
                else if (param.ParameterType == typeof(IAuditLogger))
                    arguments[i] = auditLogger;
                else if (param.ParameterType == typeof(IResponseCache))
                    arguments[i] = responseCache;
                else if (param.ParameterType == typeof(IPerformanceMetricsCollector))
                    arguments[i] = metricsCollector;
                else if (param.ParameterType == typeof(IApiKeyManager))
                    arguments[i] = apiKeyManager;
                else
                {
                    arguments[i] = _serviceProvider.GetService(param.ParameterType);
                    if (arguments[i] == null && !param.HasDefaultValue)
                        throw new InvalidOperationException($"Cannot resolve parameter {param.Name} of type {param.ParameterType}");
                }
            }

            var provider = (ICompletionProvider)Activator.CreateInstance(providerType, arguments);

            if (provider is IDisposable disposable)
            {
                lock (_cacheLock)
                {
                    _disposableProviders.Add(disposable);
                }
            }

            return provider;
        }

        public IEnumerable<ICompletionProvider> GetProvidersForType(RequestType requestType)
        {
            var providers = new List<ICompletionProvider>();

            foreach (var providerName in _providerTypes.Keys)
            {
                try
                {
                    var provider = CreateProvider(providerName);
                    if (provider != null && provider.CanHandleRequest(new AiRequest { Type = requestType }))
                    {
                        providers.Add(provider);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping provider {Provider} for type {RequestType}",
                        providerName, requestType);
                }
            }

            return providers.OrderBy(p => p.Priority);
        }

        public IEnumerable<ICompletionProvider> GetAllProviders()
        {
            var providers = new List<ICompletionProvider>();

            foreach (var providerName in _providerTypes.Keys)
            {
                try
                {
                    var provider = CreateProvider(providerName);
                    if (provider != null)
                    {
                        providers.Add(provider);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to create provider {Provider}", providerName);
                }
            }

            return providers;
        }

        public async Task<bool> ValidateConfigurationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var validationResults = _validator.ValidateAllConfigurations(_config);

                var validConfigurations = validationResults
                    .Where(kvp => kvp.Value.IsValid)
                    .Select(kvp => kvp.Key)
                    .ToList();

                var invalidConfigurations = validationResults
                    .Where(kvp => !kvp.Value.IsValid)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var kvp in validationResults)
                {
                    var providerName = kvp.Key;
                    var result = kvp.Value;

                    if (result.IsValid)
                    {
                        if (result.Warnings.Any())
                        {
                            _logger.LogInformation(
                                "Configuration valid for provider {Provider} with warnings: {Warnings}",
                                providerName, string.Join("; ", result.Warnings));
                        }
                        else
                        {
                            _logger.LogDebug("Configuration valid for provider {Provider}", providerName);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Configuration invalid for provider {Provider}: {Errors}",
                            providerName, string.Join("; ", result.Errors));
                    }
                }

                _logger.LogInformation(
                    "Configuration validation completed: {Valid}/{Total} providers valid",
                    validConfigurations.Count, _config.Providers.Count);

                if (invalidConfigurations.Any())
                {
                    _logger.LogWarning("Invalid providers: {Providers}", string.Join(", ", invalidConfigurations));
                }

                await PerformLightweightConnectivityCheckAsync(validConfigurations, cancellationToken);

                return validConfigurations.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Configuration validation failed");
                return false;
            }
        }

        private async Task PerformLightweightConnectivityCheckAsync(
            List<string> providerNames,
            CancellationToken cancellationToken)
        {
            foreach (var providerName in providerNames)
            {
                try
                {
                    var config = _config.Providers[providerName];

                    var httpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
                    if (httpClientFactory != null)
                    {
                        using var client = httpClientFactory.CreateClient(providerName);

                        if (!string.IsNullOrEmpty(config.BaseUrl))
                        {
                            if (Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var uri))
                            {
                                _logger.LogDebug("Provider {Provider} configuration looks good (valid URI)", providerName);
                            }
                            else
                            {
                                _logger.LogWarning("Provider {Provider} has invalid BaseUrl: {BaseUrl}",
                                    providerName, config.BaseUrl);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Lightweight check failed for provider {Provider}", providerName);
                }
            }

            await Task.CompletedTask;
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                foreach (var provider in _providerCache.Values)
                {
                    if (provider is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                            _disposableProviders.Remove(disposable);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to dispose provider");
                        }
                    }
                }
                _providerCache.Clear();
                _logger.LogInformation("Cleared provider cache");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    ClearCache();

                    foreach (var disposable in _disposableProviders)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to dispose provider during factory disposal");
                        }
                    }
                    _disposableProviders.Clear();
                }
                _disposed = true;
            }
        }

        ~ProviderFactory()
        {
            Dispose(false);
        }
    }
}