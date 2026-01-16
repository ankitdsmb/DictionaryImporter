using System.Security.Cryptography;

namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

public class ConfigurationApiKeyManager : IApiKeyManager
{
    private readonly AiOrchestrationConfiguration _config;
    private readonly ILogger<ConfigurationApiKeyManager> _logger;
    private readonly Dictionary<string, string> _apiKeys = new();
    private readonly Dictionary<string, List<ApiKeyHistory>> _keyHistory = new();

    public ConfigurationApiKeyManager(
        IOptions<AiOrchestrationConfiguration> config,
        ILogger<ConfigurationApiKeyManager> logger)
    {
        _config = config.Value;
        _logger = logger;

        InitializeApiKeys();
    }

    private void InitializeApiKeys()
    {
        foreach (var providerConfig in _config.Providers.Values)
        {
            if (!string.IsNullOrEmpty(providerConfig.ApiKey))
            {
                _apiKeys[providerConfig.Name] = providerConfig.ApiKey;

                var history = new ApiKeyHistory
                {
                    ProviderName = providerConfig.Name,
                    KeyIdentifier = GenerateKeyIdentifier(providerConfig.ApiKey),
                    KeyType = "Primary",
                    KeyHash = HashApiKey(providerConfig.ApiKey),
                    KeyLastFour = GetLastFour(providerConfig.ApiKey),
                    CreatedAt = DateTime.UtcNow,
                    ActivatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                if (!_keyHistory.ContainsKey(providerConfig.Name))
                {
                    _keyHistory[providerConfig.Name] = new List<ApiKeyHistory>();
                }

                _keyHistory[providerConfig.Name].Add(history);
            }
        }

        _logger.LogInformation("Initialized API keys for {Count} providers", _apiKeys.Count);
    }

    public Task<string> GetCurrentApiKeyAsync(string providerName)
    {
        if (_apiKeys.TryGetValue(providerName, out var apiKey))
        {
            return Task.FromResult(apiKey);
        }

        throw new InvalidOperationException($"No API key configured for provider: {providerName}");
    }

    public Task<string> GetApiKeyAsync(string providerName, bool useBackup = false)
    {
        return GetCurrentApiKeyAsync(providerName);
    }

    public async Task RotateApiKeyAsync(string providerName)
    {
        try
        {
            _logger.LogInformation("Rotating API key for provider: {Provider}", providerName);

            var currentKey = await GetCurrentApiKeyAsync(providerName);

            if (_keyHistory.TryGetValue(providerName, out var history))
            {
                var currentEntry = history.FirstOrDefault(h => h.IsActive);
                if (currentEntry != null)
                {
                    currentEntry.DeactivatedAt = DateTime.UtcNow;
                    currentEntry.DeactivationReason = "Rotated";
                    currentEntry.IsActive = false;
                }
            }

            var newKey = GenerateSecureApiKey();

            _apiKeys[providerName] = newKey;

            var newHistory = new ApiKeyHistory
            {
                ProviderName = providerName,
                KeyIdentifier = GenerateKeyIdentifier(newKey),
                KeyType = "Primary",
                KeyHash = HashApiKey(newKey),
                KeyLastFour = GetLastFour(newKey),
                CreatedAt = DateTime.UtcNow,
                ActivatedAt = DateTime.UtcNow,
                IsActive = true
            };

            if (!_keyHistory.ContainsKey(providerName))
            {
                _keyHistory[providerName] = new List<ApiKeyHistory>();
            }

            _keyHistory[providerName].Add(newHistory);

            _logger.LogInformation("API key rotated successfully for provider: {Provider}", providerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate API key for provider: {Provider}", providerName);
            throw;
        }
    }

    public Task<IEnumerable<ApiKeyInfo>> GetApiKeyHistoryAsync(string providerName)
    {
        if (_keyHistory.TryGetValue(providerName, out var history))
        {
            var apiKeyInfos = history.Select(h => new ApiKeyInfo
            {
                ProviderName = h.ProviderName,
                KeyIdentifier = h.KeyIdentifier,
                KeyType = h.KeyType,
                CreatedAt = h.CreatedAt,
                ActivatedAt = h.ActivatedAt,
                DeactivatedAt = h.DeactivatedAt,
                DeactivationReason = h.DeactivationReason,
                IsActive = h.IsActive
            });

            return Task.FromResult(apiKeyInfos);
        }

        return Task.FromResult(Enumerable.Empty<ApiKeyInfo>());
    }

    public Task<bool> ValidateApiKeyAsync(string providerName, string apiKey)
    {
        if (_apiKeys.TryGetValue(providerName, out var storedKey))
        {
            return Task.FromResult(storedKey == apiKey);
        }

        return Task.FromResult(false);
    }

    private string GenerateSecureApiKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string GenerateKeyIdentifier(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hash, 0, 8).Replace("+", "").Replace("/", "").Replace("=", "");
    }

    private string HashApiKey(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hash);
    }

    private string GetLastFour(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 4)
            return "****";

        return apiKey.Substring(apiKey.Length - 4);
    }

    private class ApiKeyHistory
    {
        public string ProviderName { get; set; }
        public string KeyIdentifier { get; set; }
        public string KeyType { get; set; }
        public string KeyHash { get; set; }
        public string KeyLastFour { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ActivatedAt { get; set; }
        public DateTime? DeactivatedAt { get; set; }
        public string DeactivationReason { get; set; }
        public bool IsActive { get; set; }
    }
}