using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using DictionaryImporter.AITextKit.AI.Configuration;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations
{
    public sealed class ConfigurationApiKeyManager : IApiKeyManager
    {
        private readonly AiOrchestrationConfiguration _config;
        private readonly ILogger<ConfigurationApiKeyManager> _logger;

        private readonly ConcurrentDictionary<string, string> _keys =
            new(StringComparer.OrdinalIgnoreCase);

        public ConfigurationApiKeyManager(
            AiOrchestrationConfiguration config,
            ILogger<ConfigurationApiKeyManager> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeKeysFromConfig();
        }

        private void InitializeKeysFromConfig()
        {
            _keys.Clear();

            if (_config.Providers == null || _config.Providers.Count == 0)
            {
                _logger.LogWarning("AI provider configurations not loaded. Providers count = 0");
                _logger.LogInformation("Initialized API keys for 0 providers");
                return;
            }

            foreach (var kvp in _config.Providers)
            {
                var providerName = kvp.Key;
                var providerConfig = kvp.Value;

                if (providerConfig == null)
                    continue;

                var apiKey = ResolveApiKey(providerName, providerConfig.ApiKey);

                if (!string.IsNullOrWhiteSpace(apiKey))
                    _keys[providerName] = apiKey;
            }

            _logger.LogInformation("Initialized API keys for {Count} providers", _keys.Count);
        }

        public Task<string> GetCurrentApiKeyAsync(string providerName)
        {
            return GetApiKeyAsync(providerName, useFallback: true);
        }

        public Task<string> GetApiKeyAsync(string providerName, bool useFallback)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                throw new ArgumentException("Provider name cannot be null or empty", nameof(providerName));

            if (_keys.TryGetValue(providerName, out var key) && !string.IsNullOrWhiteSpace(key))
                return Task.FromResult(key);

            if (useFallback)
            {
                var envKey = ResolveApiKey(providerName, configuredKey: null);
                if (!string.IsNullOrWhiteSpace(envKey))
                    return Task.FromResult(envKey);
            }

            throw new InvalidOperationException($"No API key configured for provider: {providerName}");
        }

        public Task RotateApiKeyAsync(string providerName)
        {
            // Config based keys => rotation not supported
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ApiKeyInfo>> GetApiKeyHistoryAsync(string providerName)
        {
            // Config based keys => no history tracking
            IEnumerable<ApiKeyInfo> empty = Array.Empty<ApiKeyInfo>();
            return Task.FromResult(empty);
        }

        public Task<bool> ValidateApiKeyAsync(string providerName, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return Task.FromResult(false);

            if (string.IsNullOrWhiteSpace(apiKey))
                return Task.FromResult(false);

            if (apiKey.StartsWith("${", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        private static string ResolveApiKey(string providerName, string configuredKey)
        {
            if (!string.IsNullOrWhiteSpace(configuredKey) &&
                !configuredKey.StartsWith("${", StringComparison.OrdinalIgnoreCase))
            {
                return configuredKey.Trim();
            }

            var envVar = providerName switch
            {
                "OpenRouter" => "OPENROUTER_API_KEY",
                "Gemini" => "GEMINI_API_KEY",
                "Anthropic" => "ANTHROPIC_API_KEY",
                _ => providerName.ToUpperInvariant() + "_API_KEY"
            };

            return Environment.GetEnvironmentVariable(envVar);
        }
    }
}