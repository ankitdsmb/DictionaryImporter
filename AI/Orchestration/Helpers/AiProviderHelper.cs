using DictionaryImporter.AI.Core.Exceptions;

namespace DictionaryImporter.AI.Orchestration.Helpers
{
    public static class AiProviderHelper
    {
        #region Request Validation

        public static void ValidateCommonRequest(AiRequest request, ProviderCapabilities capabilities, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("Prompt cannot be empty");

            if (request.MaxTokens > capabilities.MaxTokensLimit)
            {
                logger.LogWarning(
                    "Requested {Requested} tokens exceeds provider limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, capabilities.MaxTokensLimit, capabilities.MaxTokensLimit);
            }
        }

        public static void ValidateRequestWithLengthLimit(AiRequest request, int maxLength, string providerName)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("Prompt cannot be empty");

            if (request.Prompt.Length > maxLength)
                throw new ArgumentException($"{providerName} prompt exceeds limit of {maxLength} characters. Length: {request.Prompt.Length}");
        }

        #endregion Request Validation

        #region HTTP Request Creation

        public static HttpRequestMessage CreateJsonRequest(object payload, string url, JsonSerializerOptions jsonOptions = null)
        {
            var options = jsonOptions ?? new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            return new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, options),
                    Encoding.UTF8,
                    "application/json")
            };
        }

        public static HttpRequestMessage CreateJsonRequestWithSnakeCase(object payload, string url)
        {
            return CreateJsonRequest(payload, url, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            });
        }

        public static void SetCommonHeaders(HttpClient client, string authHeader, string authValue)
        {
            client.DefaultRequestHeaders.Clear();
            if (!string.IsNullOrEmpty(authHeader) && !string.IsNullOrEmpty(authValue))
                client.DefaultRequestHeaders.Add(authHeader, authValue);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
        }

        public static void SetBearerAuth(HttpClient client, string apiKey)
        {
            SetCommonHeaders(client, "Authorization", $"Bearer {apiKey}");
        }

        public static void SetApiKeyAuth(HttpClient client, string apiKey)
        {
            SetCommonHeaders(client, "api-key", apiKey);
        }

        #endregion HTTP Request Creation

        #region Error Handling

        public static string GetCommonErrorCode(Exception ex)
        {
            return ex switch
            {
                ProviderQuotaExceededException => "QUOTA_EXCEEDED",
                RateLimitExceededException => "RATE_LIMIT_EXCEEDED",
                HttpRequestException httpEx => httpEx.StatusCode.HasValue ? $"HTTP_{httpEx.StatusCode.Value}" : "HTTP_ERROR",
                TimeoutException => "TIMEOUT",
                JsonException => "INVALID_RESPONSE",
                FormatException => "INVALID_RESPONSE",
                ArgumentException => "INVALID_REQUEST",
                _ => "UNKNOWN_ERROR"
            };
        }

        public static bool ShouldFallbackCommon(Exception exception)
        {
            if (exception is ProviderQuotaExceededException || exception is RateLimitExceededException)
                return true;

            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("429") || message.Contains("401") || message.Contains("403") ||
                       message.Contains("503") || message.Contains("quota") || message.Contains("limit") ||
                       message.Contains("rate limit") || message.Contains("free tier") ||
                       message.Contains("insufficient_quota") || message.Contains("monthly");
            }

            if (exception is TimeoutException || exception is TaskCanceledException)
                return true;

            return false;
        }

        public static AiResponse CreateErrorResponse(
            Exception ex,
            string providerName,
            string defaultModel,
            TimeSpan elapsedTime,
            AiRequest request = null,
            string actualModel = null)
        {
            return new AiResponse
            {
                Content = string.Empty,
                Provider = providerName,
                Model = actualModel ?? defaultModel,
                ProcessingTime = elapsedTime,
                IsSuccess = false,
                ErrorCode = GetCommonErrorCode(ex),
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = actualModel ?? defaultModel,
                    ["error_type"] = ex.GetType().Name,
                    ["stack_trace"] = ex.StackTrace ?? string.Empty
                }
            };
        }

        #endregion Error Handling

        #region Response Processing

        public static AiResponse CreateSuccessResponse(
            string content,
            string providerName,
            string model,
            long tokensUsed,
            TimeSpan elapsedTime,
            decimal estimatedCost,
            Dictionary<string, object> additionalMetadata = null)
        {
            var metadata = new Dictionary<string, object>
            {
                ["model"] = model,
                ["tokens_used"] = tokensUsed,
                ["estimated_cost"] = estimatedCost,
                [providerName.ToLowerInvariant().Replace(" ", "_")] = true
            };

            if (additionalMetadata != null)
            {
                foreach (var kvp in additionalMetadata)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }

            return new AiResponse
            {
                Content = content.Trim(),
                Provider = providerName,
                Model = model,
                TokensUsed = tokensUsed,
                ProcessingTime = elapsedTime,
                IsSuccess = true,
                EstimatedCost = estimatedCost,
                Metadata = metadata
            };
        }

        public static long EstimateTokenUsage(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var characters = text.Length;
            var tokensFromWords = (long)(words * 1.3);
            var tokensFromChars = characters / 4;

            return Math.Max(tokensFromWords, tokensFromChars);
        }

        public static (long inputTokens, long outputTokens) EstimateTokenUsageFromPromptAndResponse(string prompt, string response)
        {
            return (EstimateTokenUsage(prompt), EstimateTokenUsage(response));
        }

        #endregion Response Processing

        #region JSON Response Parsing

        public static string ExtractTextFromJsonElement(JsonElement element, params string[] propertyPath)
        {
            var current = element;
            foreach (var property in propertyPath)
            {
                if (!current.TryGetProperty(property, out current))
                    return string.Empty;
            }

            return current.GetString() ?? string.Empty;
        }

        public static bool HasError(JsonElement root, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (root.TryGetProperty("error", out var errorElement))
            {
                errorMessage = errorElement.TryGetProperty("message", out var message)
                    ? message.GetString() ?? "Unknown error"
                    : errorElement.GetString() ?? "Unknown error";
                return true;
            }

            if (root.TryGetProperty("detail", out var detailElement))
            {
                errorMessage = detailElement.GetString() ?? "Unknown error";
                return true;
            }

            if (root.TryGetProperty("err", out var errElement))
            {
                errorMessage = errElement.GetString() ?? "Unknown error";
                return true;
            }

            return false;
        }

        public static bool IsQuotaError(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage)) return false;

            var lowerMessage = errorMessage.ToLowerInvariant();
            return lowerMessage.Contains("quota") ||
                   lowerMessage.Contains("limit") ||
                   lowerMessage.Contains("rate limit") ||
                   lowerMessage.Contains("free tier") ||
                   lowerMessage.Contains("monthly") ||
                   lowerMessage.Contains("insufficient_quota");
        }

        public static string ParseJsonResponse(JsonDocument jsonDoc,
            Func<JsonElement, string> textExtractor,
            Func<JsonElement, long> tokenExtractor = null)
        {
            var root = jsonDoc.RootElement;

            if (HasError(root, out var errorMessage))
            {
                if (IsQuotaError(errorMessage))
                    throw new ProviderQuotaExceededException("Provider", $"API error: {errorMessage}");
                throw new HttpRequestException($"API error: {errorMessage}");
            }

            return textExtractor(root);
        }

        #endregion JSON Response Parsing

        #region Provider-Specific Helper Methods

        public static (string AudioInput, int EstimatedDuration) ProcessAudioInput(AiRequest request)
        {
            string audioInput;
            int estimatedDuration;

            if (IsAudioUrl(request.Prompt))
            {
                audioInput = request.Prompt;
                estimatedDuration = 60;
            }
            else if (IsAudioBase64(request.Prompt))
            {
                audioInput = request.Prompt;
                estimatedDuration = EstimateAudioDurationFromBase64(request.Prompt);
            }
            else if (request.AudioData != null && request.AudioData.Length > 0)
            {
                audioInput = Convert.ToBase64String(request.AudioData);
                estimatedDuration = EstimateAudioDurationFromBytes(request.AudioData);
            }
            else
            {
                throw new ArgumentException("Audio URL, base64 data, or AudioData required for audio transcription");
            }

            return (audioInput, estimatedDuration);
        }

        private static bool IsAudioUrl(string input)
        {
            return !string.IsNullOrEmpty(input) &&
                   (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    input.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAudioBase64(string input)
        {
            return !string.IsNullOrEmpty(input) &&
                   (input.StartsWith("data:audio/", StringComparison.OrdinalIgnoreCase) ||
                    (input.Length > 100 && input.Contains("base64")));
        }

        private static int EstimateAudioDurationFromBase64(string base64Data)
        {
            try
            {
                string base64String;
                if (base64Data.StartsWith("data:audio/"))
                {
                    var parts = base64Data.Split(',');
                    if (parts.Length < 2) return 60;
                    base64String = parts[1];
                }
                else
                {
                    base64String = base64Data;
                }

                var dataLength = base64String.Length * 3 / 4;
                var kilobytes = dataLength / 1024.0;
                var minutes = kilobytes / 1024.0;
                return (int)Math.Max(1, minutes * 60);
            }
            catch
            {
                return 60;
            }
        }

        private static int EstimateAudioDurationFromBytes(byte[] audioData)
        {
            try
            {
                var megabytes = audioData.Length / (1024.0 * 1024.0);
                return (int)Math.Max(1, megabytes * 60);
            }
            catch
            {
                return 60;
            }
        }

        public static string GetMimeType(string imageFormat)
        {
            if (string.IsNullOrEmpty(imageFormat)) return "image/jpeg";

            return imageFormat.ToLower() switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        #endregion Provider-Specific Helper Methods
    }
}