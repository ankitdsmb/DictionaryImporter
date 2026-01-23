namespace DictionaryImporter.Gateway.Ai.Abstractions;

public interface IAiProviderClient
{
    string Name { get; }

    bool Supports(AiCapability capability);

    Task<AiProviderResult> ExecuteAsync(AiGatewayRequest request, CancellationToken ct);
}