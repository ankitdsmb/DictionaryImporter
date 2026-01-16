using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace DictionaryImporter.AITextKit.AI.Orchestration.Providers.Base
{
    public abstract class TextToSpeechProviderBase(
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
        public override ProviderType Type => ProviderType.TextToSpeech;
        public override bool SupportsTextToSpeech => true;

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.TextToSpeech = true;
            Capabilities.SupportedAudioFormats.AddRange(new[] { "mp3", "wav" });
        }

        protected override void ValidateRequest(AiRequest request)
        {
            base.ValidateRequest(request);

            if (request.Prompt.Length > 5000)
                Logger.LogWarning("Text length {Length} exceeds recommended limit for TTS", request.Prompt.Length);
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            tokenUsage = 0;
            return string.Empty;
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new { };
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ValidateRequest(request);

                if (!IsEnabled)
                    throw new InvalidOperationException($"{ProviderName} provider is disabled");

                var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
                if (!quotaCheck.CanProceed)
                    throw new ProviderQuotaExceededException(ProviderName, "Quota exceeded");

                if (Configuration.EnableCaching)
                {
                    var cachedResponse = await TryGetCachedResponseAsync(request);
                    if (cachedResponse != null)
                        return cachedResponse;
                }

                var audioData = await GenerateAudioAsync(request, cancellationToken);
                stopwatch.Stop();

                var aiResponse = new AiResponse
                {
                    Content = Convert.ToBase64String(audioData),
                    Provider = ProviderName,
                    Model = Configuration.Model ?? GetDefaultModel(),
                    TokensUsed = request.Prompt.Length,
                    ProcessingTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    EstimatedCost = EstimateCost(request.Prompt.Length, 0),
                    AudioData = audioData,
                    AudioFormat = "mp3",
                    Metadata = new Dictionary<string, object>
                    {
                        ["model"] = Configuration.Model ?? GetDefaultModel(),
                        ["characters_used"] = request.Prompt.Length,
                        ["text_to_speech"] = true,
                        ["audio_format"] = "mp3"
                    }
                };

                await RecordUsageAsync(request, aiResponse, stopwatch.Elapsed, request.Context?.UserId);

                if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
                {
                    await CacheResponseAsync(request, aiResponse,
                        TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
                }

                return aiResponse;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Logger.LogError(ex, "{Provider} provider failed for request {RequestId}",
                    ProviderName, request.Context?.RequestId);

                if (ShouldFallback(ex))
                    throw;

                return CreateErrorResponse(ex, stopwatch.Elapsed, request);
            }
        }

        protected abstract Task<byte[]> GenerateAudioAsync(AiRequest request, CancellationToken cancellationToken);

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var costPerCharacter = 0.00003m;
            return inputTokens * costPerCharacter;
        }
    }
}