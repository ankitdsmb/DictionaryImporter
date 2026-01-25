namespace DictionaryImporter.Gateway.Ai.Configuration;

public sealed class AiGatewayOptions
{
    public List<AiProviderConfig> Providers { get; init; } = [];
}