namespace DictionaryImporter.Gateway.Ai.Abstractions;

public sealed class AiGatewayResponse
{
    public bool IsSuccess { get; init; }

    public string? OutputText { get; init; }

    public byte[]? OutputBytes { get; init; }
    public string? OutputMimeType { get; init; }

    public List<AiProviderResult> ProviderResults { get; init; } = [];

    public string? FinalProvider { get; init; }
    public string? FinalModel { get; init; }

    public string? Error { get; init; }
}