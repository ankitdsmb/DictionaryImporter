namespace DictionaryImporter.AITextKit.AI.Configuration;

public class SecurityConfiguration
{
    public bool EnableApiKeyRotation { get; set; } = false;
    public int ApiKeyRotationDays { get; set; } = 30;
    public string KeyVaultUrl { get; set; }
    public bool EnableContentValidation { get; set; } = true;
    public List<string> BlockedPatterns { get; set; } = new();
    public bool EnableUserQuotas { get; set; } = true;
    public int DefaultUserRequestsPerDay { get; set; } = 100;
    public long DefaultUserTokensPerDay { get; set; } = 100000;
}