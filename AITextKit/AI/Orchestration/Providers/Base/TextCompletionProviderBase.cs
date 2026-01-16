using ILogger = Microsoft.Extensions.Logging.ILogger;
using JsonException = System.Text.Json.JsonException;

namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers.Base
{
    /// <summary>
    /// Base class for text completion AI providers
    /// </summary>
    public abstract class TextCompletionProviderBase(
        HttpClient httpClient,
        ILogger logger,
        IOptions<ProviderConfiguration> configuration,
        IQuotaManager quotaManager = null,
        IAuditLogger auditLogger = null,
        IResponseCache responseCache = null,
        IPerformanceMetricsCollector metricsCollector = null,
        IApiKeyManager apiKeyManager = null)
        : AiProviderBase(httpClient, logger, configuration, quotaManager, auditLogger,
            responseCache, metricsCollector, apiKeyManager)
    {
        public override ProviderType Type => ProviderType.TextCompletion;

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextCompletion = true;
            Capabilities.MaxTokensLimit = 4000;
        }

        protected override void ValidateRequest(AiRequest request)
        {
            base.ValidateRequest(request);

            if (request.MaxTokens > Capabilities.MaxTokensLimit)
            {
                Logger.LogWarning(
                    "Requested {Requested} tokens exceeds {Provider} limit of {Limit}. Using {Limit} instead.",
                    request.MaxTokens, ProviderName, Capabilities.MaxTokensLimit, Capabilities.MaxTokensLimit);
            }
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;

            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);
                var root = jsonDoc.RootElement;

                if (AiProviderHelper.HasError(root, out var errorMessage))
                {
                    if (AiProviderHelper.IsQuotaError(errorMessage))
                        throw new ProviderQuotaExceededException(ProviderName, $"{ProviderName} error: {errorMessage}");

                    throw new HttpRequestException($"{ProviderName} API error: {errorMessage}");
                }

                var result = ExtractCompletionText(root);

                if (tokenUsage == 0)
                {
                    tokenUsage = EstimateTokenUsage(result);
                }

                return result;
            }
            catch (JsonException ex)
            {
                Logger.LogError(ex, "Failed to parse {Provider} JSON response", ProviderName);
                throw new FormatException($"Invalid {ProviderName} response format");
            }
        }

        protected abstract string ExtractCompletionText(JsonElement rootElement);

        protected virtual long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("total_tokens", out var totalTokens))
                    return totalTokens.GetInt64();

                if (usage.TryGetProperty("completion_tokens", out var completionTokens))
                    return completionTokens.GetInt64();
            }

            if (rootElement.TryGetProperty("tokens", out var tokens))
                return tokens.GetInt64();

            return 0;
        }
    }
}