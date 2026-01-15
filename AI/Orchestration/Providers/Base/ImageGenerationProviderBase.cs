using System;
using System.Text.Json;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Orchestration.Providers.Base
{
    public abstract class ImageGenerationProviderBase(
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
        public override ProviderType Type => ProviderType.ImageGeneration;
        public override bool SupportsImages => true;

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ImageGeneration = true;
            Capabilities.SupportedImageFormats.AddRange(new[] { "png", "jpg", "jpeg" });
            Capabilities.MaxImageSize = 1024;
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request, CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                ValidateRequest(request);
                EnsureProviderEnabled();
                await CheckAndEnforceQuotaAsync(request);

                var cached = await TryGetCachedResponseAsync(request);
                if (cached != null) return cached;

                var imageData = await GenerateImageAsync(request, cancellationToken);
                stopwatch.Stop();

                var response = new AiResponse
                {
                    Content = Convert.ToBase64String(imageData),
                    Provider = ProviderName,
                    Model = Configuration.Model ?? GetDefaultModel(),
                    TokensUsed = EstimateTokenUsage(request.Prompt),
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    EstimatedCost = EstimateCost(EstimateTokenUsage(request.Prompt), 0),
                    ImageData = imageData,
                    ImageFormat = "png",
                    Metadata = new Dictionary<string, object>
                    {
                        ["model"] = Configuration.Model ?? GetDefaultModel(),
                        ["image_generation"] = true,
                        ["image_format"] = "png",
                        ["image_size"] = imageData.Length
                    }
                };

                await RecordUsageAsync(request, response, stopwatch.Elapsed, request.Context?.UserId);

                if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
                {
                    await CacheResponseAsync(request, response,
                        TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "{Provider} image generation failed", ProviderName);

                if (ShouldFallback(ex)) throw;
                return CreateErrorResponse(ex, stopwatch.Elapsed, request);
            }
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new { };
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;
            return string.Empty;
        }

        protected abstract Task<byte[]> GenerateImageAsync(
            AiRequest request, CancellationToken cancellationToken);

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            return 0.02m;
        }
    }
}