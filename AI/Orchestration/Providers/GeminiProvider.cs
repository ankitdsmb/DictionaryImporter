using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Providers.Base;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("Gemini", Priority = 3, SupportsCaching = true)]
    public class GeminiProvider : ChatCompletionProviderBase
    {
        private const string DefaultModel = "gemini-pro";
        private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        public override string ProviderName => "Gemini";
        public override int Priority => 3;
        public override bool SupportsVision => true;

        public GeminiProvider(
            HttpClient httpClient,
            ILogger<GeminiProvider> logger,
            IOptions<ProviderConfiguration> configuration,
            IQuotaManager quotaManager = null,
            IAuditLogger auditLogger = null,
            IResponseCache responseCache = null,
            IPerformanceMetricsCollector metricsCollector = null,
            IApiKeyManager apiKeyManager = null)
            : base(httpClient, logger, configuration, quotaManager, auditLogger,
                  responseCache, metricsCollector, apiKeyManager)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("Gemini API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ImageAnalysis = true;
            Capabilities.MaxTokensLimit = 32768;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "ja", "ko", "zh" });
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override HttpRequestMessage CreateHttpRequest(object payload)
        {
            var model = Configuration.Model ?? DefaultModel;
            var url = (Configuration.BaseUrl ?? DefaultBaseUrl).Replace("{model}", model);
            url = $"{url}?key={Configuration.ApiKey}";

            return base.CreateHttpRequest(payload);
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            if (request.ImageData != null || request.ImageUrls?.Count > 0)
                return CreateVisionPayload(request);

            return CreateTextPayload(request);
        }

        private object CreateTextPayload(AiRequest request)
        {
            return new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = request.Prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                    temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                    topP = 0.95,
                    topK = 40
                },
                safetySettings = GetSafetySettings()
            };
        }

        private object CreateVisionPayload(AiRequest request)
        {
            var parts = new List<object>
            {
                new { text = request.Prompt }
            };

            if (request.ImageData != null)
            {
                var base64Image = Convert.ToBase64String(request.ImageData);
                parts.Add(new
                {
                    inlineData = new
                    {
                        mimeType = GetMimeType(request.ImageFormat),
                        data = base64Image
                    }
                });
            }

            return new
            {
                contents = new[]
                {
                    new { parts }
                },
                generationConfig = new
                {
                    maxOutputTokens = Math.Min(request.MaxTokens, Capabilities.MaxTokensLimit),
                    temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                    topP = 0.95,
                    topK = 40
                },
                safetySettings = GetSafetySettings()
            };
        }

        private object[] GetSafetySettings()
        {
            return new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_MEDIUM_AND_ABOVE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_MEDIUM_AND_ABOVE" }
            };
        }

        private string GetMimeType(string imageFormat)
        {
            return imageFormat?.ToLower() switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "webp" => "image/webp",
                _ => "image/jpeg"
            };
        }

        protected override string ExtractCompletionText(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("candidates", out var candidates))
            {
                var firstCandidate = candidates.EnumerateArray().FirstOrDefault();
                if (firstCandidate.TryGetProperty("content", out var content))
                {
                    if (content.TryGetProperty("parts", out var parts))
                    {
                        var firstPart = parts.EnumerateArray().FirstOrDefault();
                        if (firstPart.TryGetProperty("text", out var text))
                        {
                            return text.GetString() ?? string.Empty;
                        }
                    }
                }
            }

            throw new FormatException("Could not find valid response content in Gemini response");
        }

        protected override long EstimateTokenUsageFromResponse(JsonElement rootElement)
        {
            if (rootElement.TryGetProperty("usageMetadata", out var usageMetadata))
            {
                return usageMetadata.GetProperty("totalTokenCount").GetInt64();
            }

            return base.EstimateTokenUsageFromResponse(rootElement);
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("gemini-1.5") || model.Contains("gemini-pro"))
            {
                var inputCostPerToken = 0.000000125m;
                var outputCostPerToken = 0.000000375m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }
    }
}