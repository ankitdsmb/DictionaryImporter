using System.Text;
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
    [Provider("ElevenLabs", Priority = 15, SupportsCaching = true)]
    public class ElevenLabsProvider : TextToSpeechProviderBase
    {
        private const string DefaultVoice = "21m00Tcm4TlvDq8ikWAM";
        private const string DefaultBaseUrl = "https://api.elevenlabs.io/v1/text-to-speech";

        public override string ProviderName => "ElevenLabs";
        public override int Priority => 15;
        public override bool SupportsAudio => true;
        public override bool SupportsTextToSpeech => true;

        public ElevenLabsProvider(
            HttpClient httpClient,
            ILogger<ElevenLabsProvider> logger,
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
                Logger.LogWarning("ElevenLabs API key not configured. Provider will be disabled.");
                Configuration.IsEnabled = false;
                return;
            }
        }

        protected override void ConfigureCapabilities()
        {
            base.ConfigureCapabilities();
            Capabilities.SupportedAudioFormats.AddRange(new[] { "mp3", "wav", "flac", "m4a" });
        }

        protected override void ConfigureAuthentication()
        {
            HttpClient.DefaultRequestHeaders.Add("xi-api-key", GetApiKey());
            HttpClient.DefaultRequestHeaders.Add("Accept", "audio/mpeg");
        }

        protected override object CreateRequestPayload(AiRequest request)
        {
            return new { };
        }

        protected override string GetDefaultBaseUrl() => DefaultBaseUrl;

        protected override string GetDefaultModel() => DefaultVoice;

        protected override async Task<byte[]> GenerateAudioAsync(
            AiRequest request, CancellationToken cancellationToken)
        {
            var voiceId = Configuration.Model ?? DefaultVoice;
            var url = $"{Configuration.BaseUrl ?? DefaultBaseUrl}/{voiceId}";

            var payload = new
            {
                text = request.Prompt,
                model_id = "eleven_monolingual_v1",
                voice_settings = new
                {
                    stability = 0.5,
                    similarity_boost = 0.5,
                    style = 0.0,
                    use_speaker_boost = true
                }
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload, JsonSerializerOptions),
                    Encoding.UTF8,
                    "application/json")
            };

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken), cancellationToken);

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        protected override AiResponse CreateSuccessResponse(string content, long tokensUsed, TimeSpan elapsedTime)
        {
            var model = Configuration.Model ?? DefaultVoice;

            return new AiResponse
            {
                Content = content,
                Provider = ProviderName,
                Model = model,
                TokensUsed = tokensUsed,
                ProcessingTime = elapsedTime,
                IsSuccess = true,
                EstimatedCost = EstimateCost(tokensUsed, 0),
                AudioData = Convert.FromBase64String(content),
                AudioFormat = "mp3",
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["voice"] = model,
                    ["characters_used"] = tokensUsed,
                    ["estimated_cost"] = EstimateCost(tokensUsed, 0),
                    ["elevenlabs"] = true,
                    ["text_to_speech"] = true,
                    ["audio_format"] = "mp3",
                    ["content_type"] = "audio/mpeg",
                    ["voice_name"] = GetVoiceName(model)
                }
            };
        }

        private string GetVoiceName(string voiceId)
        {
            var voices = new Dictionary<string, string>
            {
                ["21m00Tcm4TlvDq8ikWAM"] = "Rachel",
                ["AZnzlk1XvdvUeBnXmlld"] = "Domi",
                ["EXAVITQu4vr4xnSDxMaL"] = "Bella",
                ["ErXwobaYiN019PkySvjV"] = "Antoni",
                ["MF3mGyEYCl7XYWbV9V6O"] = "Elli",
                ["TxGEqnHWrfWFTfGW9XjX"] = "Josh",
                ["VR6AewLTigWG4xSOukaG"] = "Arnold",
                ["pNInz6obpgDQGcFmaJgB"] = "Adam",
                ["yoZ06aMxZJJ28mfd3POQ"] = "Sam"
            };

            return voices.GetValueOrDefault(voiceId, "Unknown");
        }

        protected override decimal EstimateCost(long inputTokens, long outputTokens)
        {
            var voice = Configuration.Model ?? DefaultVoice;

            if (voice.Contains("21m00Tcm4TlvDq8ikWAM") || voice.Contains("AZnzlk1XvdvUeBnXmlld"))
            {
                var costPerCharacter = 0.00003m;
                return inputTokens * costPerCharacter;
            }
            else if (voice.Contains("EXAVITQu4vr4xnSDxMaL") || voice.Contains("ErXwobaYiN019PkySvjV"))
            {
                var costPerCharacter = 0.00005m;
                return inputTokens * costPerCharacter;
            }

            var defaultCostPerCharacter = 0.00004m;
            return inputTokens * defaultCostPerCharacter;
        }

        public override bool ShouldFallback(Exception exception)
        {
            if (exception is ProviderQuotaExceededException || exception is RateLimitExceededException)
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
                       message.Contains("character");
            }

            return base.ShouldFallback(exception);
        }
    }
}