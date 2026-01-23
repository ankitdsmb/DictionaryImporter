namespace DictionaryImporter.Gateway.Ai.Abstractions;

public interface IAiProviderSelector
{
    List<IAiProviderClient> Select(List<IAiProviderClient> supported, AiGatewayRequest request);
}