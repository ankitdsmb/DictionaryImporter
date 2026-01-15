using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Helpers;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("AssemblyAI", Priority = 8, SupportsCaching = true)]
    public class AssemblyAiProvider : EnhancedBaseProvider
    {
        private const string DefaultModel = "enhanced";
        private const string BaseUrl = "https://api.assemblyai.com/v2/transcript";

        public override string ProviderName => "AssemblyAI";
        public override int Priority => 8;
        public override ProviderType Type => ProviderType.AudioTranscription;
        public override bool SupportsAudio => true;
        public override bool SupportsVision => false;
        public override bool SupportsImages => false;
        public override bool SupportsTextToSpeech => false;
        public override bool SupportsTranscription => true;
        public override bool IsLocal => false;

        public AssemblyAiProvider(
            HttpClient httpClient,
            ILogger<AssemblyAiProvider> logger,
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
                Logger.LogWarning("AssemblyAI API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.AudioTranscription = true;
            Capabilities.SupportedAudioFormats.AddRange(new[] { "mp3", "wav", "flac", "m4a", "ogg", "webm" });
            Capabilities.SupportedLanguages.AddRange(new[] { "en", "es", "fr", "de", "it", "pt", "nl", "ja", "ko", "zh" });
        }

        protected override void ConfigureAuthentication()
        {
            var apiKey = GetApiKey();
            HttpClient.DefaultRequestHeaders.Clear();
            HttpClient.DefaultRequestHeaders.Add("Authorization", apiKey);
            HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Check if provider is enabled
                if (!Configuration.IsEnabled)
                    throw new InvalidOperationException("AssemblyAI provider is disabled");

                // Check quota
                var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
                if (!quotaCheck.CanProceed)
                    throw new ProviderQuotaExceededException(ProviderName,
                        $"Quota exceeded. Remaining: {quotaCheck.RemainingRequests} requests, " +
                        $"{quotaCheck.RemainingTokens} tokens. Resets in {quotaCheck.TimeUntilReset.TotalMinutes:F0} minutes.");

                // Check cache
                if (Configuration.EnableCaching)
                {
                    var cachedResponse = await TryGetCachedResponseAsync(request);
                    if (cachedResponse != null) return cachedResponse;
                }

                // Process audio input
                var (audioInput, estimatedDuration) = AiProviderHelper.ProcessAudioInput(request);

                // Submit transcription and poll for results
                var transcriptId = await SubmitTranscriptionAsync(audioInput, cancellationToken);
                var result = await PollForTranscriptionAsync(transcriptId, cancellationToken);

                stopwatch.Stop();

                // Calculate token usage
                var tokenUsage = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                // Create success response
                var aiResponse = AiProviderHelper.CreateSuccessResponse(
                    result,
                    ProviderName,
                    string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                    tokenUsage,
                    stopwatch.Elapsed,
                    EstimateCost(tokenUsage, 0),
                    new Dictionary<string, object>
                    {
                        ["assemblyai"] = true,
                        ["audio_transcription"] = true,
                        ["estimated_audio_duration_seconds"] = estimatedDuration,
                        ["transcript_id"] = transcriptId,
                        ["audio_format"] = GetAudioFormat(request),
                        ["processing_time_seconds"] = stopwatch.Elapsed.TotalSeconds
                    });

                // Record usage and cache
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
                Logger.LogError(ex, "AssemblyAI provider failed for request {RequestId}", request.Context?.RequestId);

                if (ShouldFallback(ex)) throw;

                // Create error response
                var errorResponse = AiProviderHelper.CreateErrorResponse(
                    ex,
                    ProviderName,
                    DefaultModel,
                    stopwatch.Elapsed,
                    request,
                    Configuration.Model ?? DefaultModel);

                // Log audit if available
                if (AuditLogger != null)
                {
                    var auditEntry = CreateAuditEntry(request, errorResponse, stopwatch.Elapsed, request.Context?.UserId);
                    auditEntry.ErrorCode = errorResponse.ErrorCode;
                    auditEntry.ErrorMessage = errorResponse.ErrorMessage;
                    await AuditLogger.LogRequestAsync(auditEntry);
                }

                return errorResponse;
            }
        }

        private async Task<string> SubmitTranscriptionAsync(string audioInput, CancellationToken cancellationToken)
        {
            var payload = CreateTranscriptionPayload(audioInput);
            var httpRequest = AiProviderHelper.CreateJsonRequest(payload, Configuration.BaseUrl ?? BaseUrl);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            using var jsonDoc = JsonDocument.Parse(content);
            if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
            {
                return idElement.GetString() ?? throw new InvalidOperationException("No transcript ID received");
            }

            if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.GetString() ?? "Unknown error";
                throw new HttpRequestException($"AssemblyAI submission error: {errorMessage}");
            }

            throw new InvalidOperationException("Invalid AssemblyAI response format");
        }

        private object CreateTranscriptionPayload(string audioInput)
        {
            var payload = new Dictionary<string, object>();

            if (audioInput.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                audioInput.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                payload["audio_url"] = audioInput;
            }
            else if (audioInput.StartsWith("data:audio/", StringComparison.OrdinalIgnoreCase))
            {
                payload["audio_data"] = audioInput;
            }
            else
            {
                payload["audio_data"] = $"data:audio/mp3;base64,{audioInput}";
            }

            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
            if (model == "enhanced" || model.Contains("enhanced"))
            {
                payload["language_detection"] = true;
                payload["speech_model"] = "enhanced";
            }
            else
            {
                payload["language_code"] = "en_us";
            }

            payload["punctuate"] = true;
            payload["format_text"] = true;
            payload["auto_highlights"] = false;
            payload["auto_chapters"] = false;
            payload["entity_detection"] = false;

            foreach (var setting in Configuration.AdditionalSettings.Where(s => !payload.ContainsKey(s.Key)))
            {
                payload[setting.Key] = setting.Value;
            }

            return payload;
        }

        private async Task<string> PollForTranscriptionAsync(string transcriptId, CancellationToken cancellationToken)
        {
            var baseUrl = Configuration.BaseUrl ?? BaseUrl;
            var pollUrl = $"{baseUrl}/{transcriptId}";
            var maxAttempts = 30;
            var attempt = 0;

            while (attempt < maxAttempts)
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, pollUrl);
                var response = await SendWithResilienceAsync(
                    () => HttpClient.SendAsync(httpRequest, cancellationToken),
                    cancellationToken);

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var jsonDoc = JsonDocument.Parse(content);

                var status = jsonDoc.RootElement.GetProperty("status").GetString();
                if (status == "completed")
                {
                    return jsonDoc.RootElement.GetProperty("text").GetString() ?? string.Empty;
                }
                else if (status == "error")
                {
                    var error = jsonDoc.RootElement.GetProperty("error").GetString() ?? "Unknown error";
                    throw new HttpRequestException($"AssemblyAI transcription error: {error}");
                }

                await Task.Delay(2000, cancellationToken);
                attempt++;
            }

            throw new TimeoutException("AssemblyAI transcription timeout after 60 seconds");
        }

        private string GetAudioFormat(AiRequest request)
        {
            if (!string.IsNullOrEmpty(request.AudioFormat))
                return request.AudioFormat;

            var url = request.Prompt?.ToLower() ?? "";
            if (url.Contains(".mp3")) return "mp3";
            if (url.Contains(".wav")) return "wav";
            if (url.Contains(".flac")) return "flac";
            if (url.Contains(".m4a")) return "m4a";

            if (request.Prompt?.Contains("data:audio/mp3") == true) return "mp3";
            if (request.Prompt?.Contains("data:audio/wav") == true) return "wav";
            if (request.Prompt?.Contains("data:audio/flac") == true) return "flac";
            if (request.Prompt?.Contains("data:audio/m4a") == true) return "m4a";

            return "unknown";
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;

            if (model.Contains("enhanced"))
            {
                var costPerHour = 0.75m;
                var hours = (decimal)inputTokens / 3600;
                return hours * costPerHour;
            }
            else
            {
                var costPerHour = 0.45m;
                var hours = (decimal)inputTokens / 3600;
                return hours * costPerHour;
            }
        }

        public override bool ShouldFallback(Exception exception)
        {
            return AiProviderHelper.ShouldFallbackCommon(exception) || base.ShouldFallback(exception);
        }
    }
}