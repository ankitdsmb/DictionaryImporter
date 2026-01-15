using System;
using System.Collections.Generic;
using System.Text.Json;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Orchestration.Providers.Base
{
    /// <summary>
    /// Base class for chat completion AI providers (OpenAI-compatible)
    /// </summary>
    public abstract class ChatCompletionProviderBase(
        HttpClient httpClient,
        ILogger logger,
        IOptions<ProviderConfiguration> configuration,
        IQuotaManager quotaManager = null,
        IAuditLogger auditLogger = null,
        IResponseCache responseCache = null,
        IPerformanceMetricsCollector metricsCollector = null,
        IApiKeyManager apiKeyManager = null)
        : TextCompletionProviderBase(httpClient, logger, configuration, quotaManager, auditLogger,
            responseCache, metricsCollector, apiKeyManager)
    {
        protected override object CreateRequestPayload(AiRequest request)
        {
            var messages = new List<object>();

            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                messages.Add(new { role = "system", content = request.SystemPrompt });
            }

            messages.Add(new { role = "user", content = request.Prompt });

            return new
            {
                model = Configuration.Model ?? GetDefaultModel(),
                messages = messages,
                max_tokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                temperature = Math.Clamp(request.Temperature, 0.0, 2.0),
                top_p = 0.9,
                frequency_penalty = 0.0,
                presence_penalty = 0.0,
                stream = false
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("choices", out var choices))
            {
                var firstChoice = choices.EnumerateArray().FirstOrDefault();
                if (firstChoice.TryGetProperty("message", out var message))
                {
                    if (message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? string.Empty;
                    }
                }

                if (firstChoice.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }

            throw new FormatException("Could not find completion text in response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("usage", out var usage))
            {
                long inputTokens = 0;
                long outputTokens = 0;

                if (usage.TryGetProperty("prompt_tokens", out var promptTokens))
                    inputTokens = promptTokens.GetInt64();

                if (usage.TryGetProperty("completion_tokens", out var completionTokens))
                    outputTokens = completionTokens.GetInt64();

                if (usage.TryGetProperty("total_tokens", out var totalTokens))
                    return totalTokens.GetInt64();

                return inputTokens + outputTokens;
            }

            return base.EstimateTokenUsageFromResponse(rootElement);
        }
    }
}