using DictionaryImporter.AI.Core.Exceptions;

namespace DictionaryImporter.AI.Orchestration
{
    public static class ErrorHandlingHelper
    {
        public static string GetStandardizedErrorCode(Exception ex)
        {
            if (ex is AiOrchestrationException aiEx)
                return aiEx.ErrorCode;
            else if (ex is ProviderQuotaExceededException)
                return "QUOTA_EXCEEDED";
            else if (ex is RateLimitExceededException)
                return "RATE_LIMIT_EXCEEDED";
            else if (ex is CircuitBreakerOpenException)
                return "CIRCUIT_BREAKER_OPEN";
            else if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
                return $"HTTP_{(int)httpEx.StatusCode.Value}";
            else if (ex is HttpRequestException)
                return "HTTP_ERROR";
            else if (ex is TimeoutException)
                return "TIMEOUT";
            else if (ex is TaskCanceledException || ex is OperationCanceledException)
                return "CANCELLED";
            else if (ex is JsonException)
                return "INVALID_RESPONSE";
            else if (ex is FormatException)
                return "INVALID_RESPONSE";
            else if (ex is ArgumentException)
                return "INVALID_REQUEST";
            else if (ex is InvalidOperationException)
                return "INVALID_OPERATION";
            else
                return "UNKNOWN_ERROR";
        }

        public static string GetUserFriendlyErrorMessage(string errorCode, Exception ex)
        {
            return errorCode switch
            {
                "QUOTA_EXCEEDED" => "Service quota has been exceeded. Please try again later.",
                "RATE_LIMIT_EXCEEDED" => "Too many requests. Please slow down and try again.",
                "CIRCUIT_BREAKER_OPEN" => "Service temporarily unavailable. Please try again in a few moments.",
                "TIMEOUT" => "Request timed out. Please try again.",
                "CANCELLED" => "Request was cancelled.",
                "HTTP_401" or "HTTP_403" => "Authentication failed. Please check your API keys.",
                "HTTP_429" => "Too many requests. Please slow down.",
                "HTTP_500" or "HTTP_502" or "HTTP_503" or "HTTP_504" =>
                    "Service temporarily unavailable. Please try again later.",
                "INVALID_RESPONSE" => "Received an invalid response from the service.",
                "INVALID_REQUEST" => "The request was invalid. Please check your input.",
                _ => ex?.Message ?? "An unexpected error occurred."
            };
        }

        public static bool IsRetryableError(string errorCode)
        {
            var retryableCodes = new HashSet<string>
            {
                "RATE_LIMIT_EXCEEDED",
                "CIRCUIT_BREAKER_OPEN",
                "TIMEOUT",
                "CANCELLED",
                "HTTP_429",
                "HTTP_500",
                "HTTP_502",
                "HTTP_503",
                "HTTP_504"
            };

            return retryableCodes.Contains(errorCode);
        }

        public static bool ShouldLogAsWarning(string errorCode)
        {
            var warningCodes = new HashSet<string>
            {
                "TIMEOUT",
                "CANCELLED",
                "HTTP_429",
                "RATE_LIMIT_EXCEEDED"
            };

            return warningCodes.Contains(errorCode);
        }

        public static bool ShouldLogAsError(string errorCode)
        {
            var errorCodes = new HashSet<string>
            {
                "HTTP_401",
                "HTTP_403",
                "HTTP_500",
                "INVALID_RESPONSE",
                "INVALID_REQUEST",
                "UNKNOWN_ERROR"
            };

            return errorCodes.Contains(errorCode);
        }
    }
}