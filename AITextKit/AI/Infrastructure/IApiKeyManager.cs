namespace DictionaryImporter.AITextKit.AI.Infrastructure;

public interface IApiKeyManager
{
    Task<string> GetCurrentApiKeyAsync(string providerName);

    Task<string> GetApiKeyAsync(string providerName, bool useBackup = false);

    Task RotateApiKeyAsync(string providerName);

    Task<IEnumerable<ApiKeyInfo>> GetApiKeyHistoryAsync(string providerName);

    Task<bool> ValidateApiKeyAsync(string providerName, string apiKey);
}