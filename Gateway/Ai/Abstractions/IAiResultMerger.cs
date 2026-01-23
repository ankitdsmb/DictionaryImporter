namespace DictionaryImporter.Gateway.Ai.Abstractions;

public interface IAiResultMerger
{
    AiProviderResult Merge(AiGatewayRequest request, List<AiProviderResult> results);
}