namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

public class NullApiKeyManager : IApiKeyManager
{
    public Task<string> GetCurrentApiKeyAsync(string providerName)
    {
        throw new InvalidOperationException($"No API key manager configured for provider: {providerName}");
    }

    public Task<string> GetApiKeyAsync(string providerName, bool useBackup = false)
    {
        throw new InvalidOperationException($"No API key manager configured for provider: {providerName}");
    }

    public Task RotateApiKeyAsync(string providerName)
    {
        throw new InvalidOperationException("API key rotation not supported");
    }

    public Task<IEnumerable<ApiKeyInfo>> GetApiKeyHistoryAsync(string providerName)
    {
        return Task.FromResult(Enumerable.Empty<ApiKeyInfo>());
    }

    public Task<bool> ValidateApiKeyAsync(string providerName, string apiKey)
    {
        return Task.FromResult(false);
    }
}