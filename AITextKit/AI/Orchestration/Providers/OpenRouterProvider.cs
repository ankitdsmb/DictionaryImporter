using DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers
{
    [Provider("OpenRouter", Priority = 1, SupportsCaching = true)]
    public class OpenRouterProvider : ChatCompletionProviderBase
    {
        private const string DefaultModel = "openai/gpt-3.5-turbo";
        private const string DefaultBaseUrl = "https://api.openrouter.ai/api/v1/chat/completions";
        private readonly RateLimiter _rateLimiter;

        private static readonly ConcurrentDictionary<string, DateTime> _requestTimestamps = new();

        private static readonly object _rateLimitLock = new();
        public override string ProviderName => "OpenRouter";
        public override int Priority => 1;

        public OpenRouterProvider(
            HttpClient httpClient,
            ILogger<OpenRouterProvider> logger,
            IOptions<ProviderConfiguration> configuration,
            IQuotaManager quotaManager = null,
            IAuditLogger auditLogger = null,
            IResponseCache responseCache = null,
            IPerformanceMetricsCollector metricsCollector = null,
            IApiKeyManager apiKeyManager = null)
            : base(httpClient, logger, configuration, quotaManager, auditLogger, responseCache, metricsCollector, apiKeyManager)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("OpenRouter API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }

            var maxRequestsPerMinute = Configuration.RequestsPerMinute > 0 ? Configuration.RequestsPerMinute : 60;
            _rateLimiter = new RateLimiter(maxRequestsPerMinute, TimeSpan.FromMinutes(1));
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.ChatCompletion = true;
            Capabilities.MaxTokensLimit = 4096;
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "ja", "ko", "zh" });
        }

        protected override void ConfigureAuthentication()
        {
            var apiKey = GetApiKey();
            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            HttpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://dictionary-importer.com");
            HttpClient.DefaultRequestHeaders.Add("X-Title", "Dictionary Importer");
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override void ValidateRequest(AiRequest request)
        {
            base.ValidateRequest(request);

            if (request.Prompt.Length > 32000)
                throw new ArgumentException($"Prompt exceeds OpenRouter limit of 32,000 characters. Length: {request.Prompt.Length}");
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            await ApplyRateLimitingAsync(cancellationToken);
            return await base.GetCompletionAsync(request, cancellationToken);
        }

        private async Task ApplyRateLimitingAsync(CancellationToken cancellationToken)
        {
            if (!Configuration.EnableRateLimiting) return;

            var canProceed = await _rateLimiter.WaitToProceedAsync(cancellationToken);
            if (!canProceed)
            {
                var retryAfter = _rateLimiter.RetryAfter;
                throw new RateLimitExceededException(
                    ProviderName,
                    retryAfter,
                    $"Rate limit exceeded: {_rateLimiter.Limit} requests/minute. " +
                    $"Try again in {retryAfter.TotalSeconds:F0} seconds.");
            }
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("gpt-4"))
            {
                var inputCostPerToken = 0.00003m;
                var outputCostPerToken = 0.00006m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }
            else if (model.Contains("gpt-3.5-turbo"))
            {
                var inputCostPerToken = 0.0000015m;
                var outputCostPerToken = 0.000002m;
                return inputTokens * inputCostPerToken + outputTokens * outputCostPerToken;
            }

            return base.EstimateCost(inputTokens, outputTokens);
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is ProviderQuotaExceededException ||
                exception is RateLimitExceededException)
                return true;

            if (exception is HttpRequestException httpEx)
            {
                var message = httpEx.Message.ToLowerInvariant();
                return message.Contains("429") ||
                       message.Contains("401") ||
                       message.Contains("403") ||
                       message.Contains("503") ||
                       message.Contains("quota") ||
                       message.Contains("limit") ||
                       message.Contains("rate limit") ||
                       message.Contains("insufficient_quota") ||
                       message.Contains("insufficient credits") ||
                       message.Contains("billing") ||
                       message.Contains("payment required");
            }

            if (exception is TimeoutException || exception is TaskCanceledException)
                return true;

            return base.ShouldFallback(exception);
        }

        private int GetRemainingRequests()
        {
            lock (_rateLimitLock)
            {
                var now = DateTime.UtcNow;
                var requestsThisMinute = _requestTimestamps.Count(kv =>
                    DateTime.ParseExact(kv.Key, "yyyyMMddHHmm", null) >= now.AddMinutes(-1));

                var maxRequestsPerMinute = Configuration.RequestsPerMinute > 0
                    ? Configuration.RequestsPerMinute : 60;

                return Math.Max(0, maxRequestsPerMinute - requestsThisMinute);
            }
        }
    }
}