namespace DictionaryImporter.Gateway.Ai.Abstractions
{
    public interface IAiGateway
    {
        Task<AiGatewayResponse> ExecuteAsync(AiGatewayRequest request, CancellationToken ct);

        Task<IReadOnlyList<AiGatewayResponse>> ExecuteBulkAsync(
            IReadOnlyList<AiGatewayRequest> requests,
            CancellationToken ct);
    }

    public interface IAiProviderClient
    {
        string Name { get; }

        bool Supports(AiCapability capability);

        Task<AiProviderResult> ExecuteAsync(AiGatewayRequest request, CancellationToken ct);
    }

    public interface IAiProviderSelector
    {
        List<IAiProviderClient> Select(List<IAiProviderClient> supported, AiGatewayRequest request);
    }

    public interface IAiResultMerger
    {
        AiProviderResult Merge(AiGatewayRequest request, List<AiProviderResult> results);
    }
}