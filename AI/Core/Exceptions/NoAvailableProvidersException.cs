namespace DictionaryImporter.AI.Core.Exceptions;

public class NoAvailableProvidersException : AiOrchestrationException
{
    public RequestType RequestType { get; }

    public NoAvailableProvidersException(RequestType requestType)
        : base(
            $"No available providers for request type: {requestType}",
            "NO_AVAILABLE_PROVIDERS",
            false)
    {
        RequestType = requestType;
    }
}