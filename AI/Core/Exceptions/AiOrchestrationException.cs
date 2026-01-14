namespace DictionaryImporter.AI.Core.Exceptions
{
    public abstract class AiOrchestrationException : Exception
    {
        public string ErrorCode { get; }
        public bool IsRetryable { get; }

        protected AiOrchestrationException(
            string message,
            string errorCode = "UNKNOWN_ERROR",
            bool isRetryable = false,
            Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            IsRetryable = isRetryable;
        }
    }
}