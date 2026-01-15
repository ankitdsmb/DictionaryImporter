namespace DictionaryImporter.AI.Core.Exceptions;

public class NoAvailableProvidersException(RequestType requestType) : AiOrchestrationException(
    $"No available providers for request type: {requestType}",
    "NO_AVAILABLE_PROVIDERS",
    false)
{
    public RequestType RequestType { get; } = requestType;
}