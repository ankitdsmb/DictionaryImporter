using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DictionaryImporter.AI.Configuration;
using DictionaryImporter.AI.Core.Attributes;
using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using DictionaryImporter.AI.Orchestration.Providers.Base;

namespace DictionaryImporter.AI.Orchestration.Providers
{
    [Provider("AssemblyAI", Priority = 8, SupportsCaching = true)]
    public class AssemblyAiProvider : AudioTranscriptionProviderBase
    {
        private const string DefaultModel = "enhanced";
        private const string DefaultBaseUrl = "https://api.assemblyai.com/v2/transcript";

        public AssemblyAiProvider(HttpClient httpClient, ILogger logger, IOptions<ProviderConfiguration> configuration, IQuotaManager quotaManager = null, IAuditLogger auditLogger = null, IResponseCache responseCache = null, IPerformanceMetricsCollector metricsCollector = null, IApiKeyManager apiKeyManager = null) : base(httpClient, logger, configuration, quotaManager, auditLogger, responseCache, metricsCollector, apiKeyManager)
        {
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                Logger.LogWarning("ElevenLabs API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        public override string ProviderName => "AssemblyAI";
        public override int Priority => 8;

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", GetApiKey());
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultModel;

        protected override object CreateTranscriptionPayload(AiRequest request)
        {
            var (audioInput, _) = ProcessAudioInput(request);

            var payload = new Dictionary<string, object>();

            if (audioInput.StartsWith("http"))
                payload["audio_url"] = audioInput;
            else if (audioInput.StartsWith("data:audio/"))
                payload["audio_data"] = audioInput;
            else
                payload["audio_data"] = $"data:audio/mp3;base64,{audioInput}";

            var model = Configuration.Model ?? DefaultModel;
            if (model == "enhanced")
            {
                payload["language_detection"] = true;
                payload["speech_model"] = "enhanced";
            }

            payload["punctuate"] = true;
            payload["format_text"] = true;

            return payload;
        }

        public override async Task<AiResponse> GetCompletionAsync(
            AiRequest request, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ValidateRequest(request);
                EnsureProviderEnabled();
                await CheckAndEnforceQuotaAsync(request);

                var cached = await TryGetCachedResponseAsync(request);
                if (cached != null) return cached;

                var transcriptId = await SubmitTranscriptionAsync(request, cancellationToken);

                var result = await PollForTranscriptionAsync(transcriptId, cancellationToken);
                stopwatch.Stop();

                var tokenUsage = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var response = CreateSuccessResponse(result, tokenUsage, stopwatch.Elapsed);
                response.Metadata["assemblyai"] = true;
                response.Metadata["transcript_id"] = transcriptId;

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
                Logger.LogError(ex, "AssemblyAI transcription failed");

                if (ShouldFallback(ex)) throw;
                return CreateErrorResponse(ex, stopwatch.Elapsed, request);
            }
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            var (audioInput, _) = ProcessAudioInput(request);
            var payload = new Dictionary<string, object>();

            if (audioInput.StartsWith("http"))
                payload["audio_url"] = audioInput;
            else if (audioInput.StartsWith("data:audio/"))
                payload["audio_data"] = audioInput;
            else
                payload["audio_data"] = $"data:audio/mp3;base64,{audioInput}";

            var model = Configuration.Model ?? DefaultModel;
            if (model == "enhanced")
            {
                payload["language_detection"] = true;
                payload["speech_model"] = "enhanced";
            }

            payload["punctuate"] = true;
            payload["format_text"] = true;

            return payload;
        }

        protected override string ParseResponse(string jsonResponse, out long tokenUsage)
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            var root = jsonDoc.RootElement;

            if (root.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString() ?? string.Empty;
                tokenUsage = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                return text;
            }

            throw new FormatException("Could not find text in AssemblyAI response");
        }

        private async Task<string> SubmitTranscriptionAsync(
            AiRequest request, CancellationToken cancellationToken)
        {
            var payload = CreateTranscriptionPayload(request);
            var httpRequest = CreateHttpRequest(payload);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken), cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDoc = JsonDocument.Parse(content);

            return jsonDoc.RootElement.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("No transcript ID received");
        }

        private async Task<string> PollForTranscriptionAsync(
            string transcriptId, CancellationToken cancellationToken)
        {
            var pollUrl = $"{Configuration.BaseUrl ?? DefaultBaseUrl}/{transcriptId}";

            for (int i = 0; i < 30; i++)
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, pollUrl);
                var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                using var jsonDoc = JsonDocument.Parse(content);
                var status = jsonDoc.RootElement.GetProperty("status").GetString();

                if (status == "completed")
                    return jsonDoc.RootElement.GetProperty("text").GetString() ?? string.Empty;
                else if (status == "error")
                    throw new HttpRequestException(
                        $"Transcription failed: {jsonDoc.RootElement.GetProperty("error").GetString()}");

                await Task.Delay(2000, cancellationToken);
            }

            throw new TimeoutException("Transcription timeout after 60 seconds");
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var model = Configuration.Model ?? DefaultModel;
            var costPerHour = model.Contains("enhanced") ? 0.75m : 0.45m;
            var hours = (decimal)inputTokens / 3600;
            return hours * costPerHour;
        }
    }
}