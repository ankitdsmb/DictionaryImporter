namespace DictionaryImporter.Gateway.Ai.Abstractions
{
    public interface IAiGateway
    {
        Task<AiGatewayResponse> ExecuteAsync(AiGatewayRequest request, CancellationToken ct);

        Task<IReadOnlyList<AiGatewayResponse>> ExecuteBulkAsync(
            IReadOnlyList<AiGatewayRequest> requests,
            CancellationToken ct);
    }
}