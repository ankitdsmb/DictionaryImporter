namespace DictionaryImporter.AITextKit.AI.Core.Exceptions;

public class ProviderQuotaExceededException(string providerName, string message = null) : AiOrchestrationException(
    message ?? $"Quota exceeded for provider: {providerName}",
    $"QUOTA_EXCEEDED_{providerName.ToUpper()}",
    false)
{
    public string ProviderName { get; } = providerName;
}