namespace DictionaryImporter.AITextKit.AI.Core.Exceptions
{
    public abstract class AiOrchestrationException(
        string message,
        string errorCode = "UNKNOWN_ERROR",
        bool isRetryable = false,
        Exception innerException = null)
        : Exception(message, innerException)
    {
        public string ErrorCode { get; } = errorCode;
        public bool IsRetryable { get; } = isRetryable;
    }
}