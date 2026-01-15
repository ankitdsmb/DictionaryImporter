using DictionaryImporter.AI.Orchestration.Providers.Base;
using System.Text.Json;
using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Infrastructure;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("StabilityAI", Priority = 14, SupportsCaching = true)]
    public class StabilityAiProvider : ImageGenerationProviderBase
    {
        private const string DefaultModel = "stable-diffusion-xl-1024-v1-0";
        private const string DefaultBaseUrl = "https://api.stability.ai/v1/generation/{model}/text-to-image";

        public StabilityAiProvider(HttpClient httpClient, ILogger logger, IOptions<ProviderConfiguration> configuration,
            IQuotaManager quotaManager = null, IAuditLogger auditLogger = null, IResponseCache responseCache = null,
            IPerformanceMetricsCollector metricsCollector = null, IApiKeyManager apiKeyManager = null) : base(
            httpClient, logger, configuration, quotaManager, auditLogger, responseCache, metricsCollector,
            apiKeyManager)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("Replicate API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
            }
        }

        public override string ProviderName => "StabilityAI";
        public override int Priority => 14;

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {GetApiKey()}");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override async Task<byte[]> GenerateImageAsync(
            AiRequest request, CancellationToken cancellationToken)
        {
            var model = Configuration.Model ?? DefaultModel;
            var url = (Configuration.BaseUrl ?? DefaultBaseUrl).Replace("{model}", model);

            var payload = new
            {
                text_prompts = new[]
                {
                    new { text = request.Prompt, weight = 1.0 }
                },
                cfg_scale = 7,
                height = 1024,
                width = 1024,
                steps = 30,
                samples = 1
            };

            var httpRequest = CreateHttpRequest(payload);
            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken), cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDoc = JsonDocument.Parse(content);

            var artifacts = jsonDoc.RootElement.GetProperty("artifacts");
            var firstArtifact = artifacts.EnumerateArray().First();
            var base64Image = firstArtifact.GetProperty("base64").GetString();

            return Convert.FromBase64String(base64Image ?? throw new FormatException("No image data"));
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            return 0.004m;
        }
    }
}