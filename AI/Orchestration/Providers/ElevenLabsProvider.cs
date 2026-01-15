using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

[Provider("ElevenLabs", Priority = 15, SupportsCaching = true)]
public class ElevenLabsProvider : EnhancedBaseProvider
{
    private const string DefaultVoice = "21m00Tcm4TlvDq8ikWAM";
    private const string BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech";

    public override string ProviderName => "ElevenLabs";
    public override int Priority => 15;
    public override ProviderType Type => ProviderType.TextToSpeech;
    public override bool SupportsAudio => true;
    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => true;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public ElevenLabsProvider(
        HttpClient httpClient,
        ILogger<ElevenLabsProvider> logger,
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
            Logger.LogWarning("ElevenLabs API key not configured. Provider will be disabled.");
            Configuration.IsEnabled = false;
            return;
        }
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextToSpeech = true;
        Capabilities.SupportedAudioFormats.AddRange(new[] { "mp3", "wav", "flac", "m4a" });
        Capabilities.SupportedLanguages.Add("en");
    }

    protected override void ConfigureAuthentication()
    {
        var apiKey = GetApiKey();
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        HttpClient.DefaultRequestHeaders.Add("Accept", "audio/mpeg");
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "DictionaryImporter/2.0");
    }

    public override async Task<AiResponse> GetCompletionAsync(
        AiRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("ElevenLabs provider is disabled");
            }

            var quotaCheck = await CheckQuotaAsync(request, request.Context?.UserId);
            if (!quotaCheck.CanProceed)
            {
                throw new ProviderQuotaExceededException(ProviderName,
                    $"Quota exceeded. Remaining: {quotaCheck.RemainingRequests} requests, " +
                    $"{quotaCheck.RemainingTokens} tokens. Resets in {quotaCheck.TimeUntilReset.TotalMinutes:F0} minutes.");
            }

            if (Configuration.EnableCaching)
            {
                var cachedResponse = await TryGetCachedResponseAsync(request);
                if (cachedResponse != null)
                {
                    return cachedResponse;
                }
            }

            ValidateSpeechRequest(request);

            var audioData = await GenerateSpeechAsync(request, cancellationToken);
            stopwatch.Stop();

            var aiResponse = new AiResponse
            {
                Content = Convert.ToBase64String(audioData),
                Provider = ProviderName,
                TokensUsed = request.Prompt.Length,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                EstimatedCost = EstimateCost(request.Prompt.Length, 0),
                AudioData = audioData,
                AudioFormat = "mp3",
                Metadata = new Dictionary<string, object>
                {
                    ["voice"] = Configuration.Model ?? DefaultVoice,
                    ["characters_used"] = request.Prompt.Length,
                    ["estimated_cost"] = EstimateCost(request.Prompt.Length, 0),
                    ["elevenlabs"] = true,
                    ["text_to_speech"] = true,
                    ["audio_format"] = "mp3",
                    ["content_type"] = "audio/mpeg",
                    ["voice_name"] = GetVoiceName(Configuration.Model ?? DefaultVoice)
                }
            };

            await RecordUsageAsync(request, aiResponse, stopwatch.Elapsed, request.Context?.UserId);

            if (Configuration.EnableCaching && Configuration.CacheDurationMinutes > 0)
            {
                await CacheResponseAsync(
                    request,
                    aiResponse,
                    TimeSpan.FromMinutes(Configuration.CacheDurationMinutes));
            }

            return aiResponse;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "ElevenLabs provider failed for request {RequestId}", request.Context?.RequestId);

            if (ShouldFallback(ex))
            {
                throw;
            }

            var errorResponse = new AiResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                Model = Configuration.Model ?? DefaultVoice,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = false,
                ErrorCode = GetErrorCode(ex),
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = Configuration.Model ?? DefaultVoice,
                    ["error_type"] = ex.GetType().Name,
                    ["stack_trace"] = ex.StackTrace
                }
            };

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

    private void ValidateSpeechRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Speech text cannot be empty");

        if (request.Prompt.Length > 5000)
        {
            Logger.LogWarning("Text length {Length} exceeds recommended limit for TTS", request.Prompt.Length);
        }
    }

    private async Task<byte[]> GenerateSpeechAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var voiceId = string.IsNullOrEmpty(Configuration.Model) ? DefaultVoice : Configuration.Model;
        var baseUrl = Configuration.BaseUrl ?? BaseUrl;
        var url = $"{baseUrl}/{voiceId}";

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
            () => HttpClient.SendAsync(httpRequest, cancellationToken),
            cancellationToken);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
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

    private string GetErrorCode(Exception ex)
    {
        return ex switch
        {
            ProviderQuotaExceededException => "QUOTA_EXCEEDED",
            RateLimitExceededException => "RATE_LIMIT_EXCEEDED",
            HttpRequestException httpEx => httpEx.StatusCode.HasValue ? $"HTTP_{httpEx.StatusCode.Value}" : "HTTP_ERROR",
            TimeoutException => "TIMEOUT",
            JsonException => "INVALID_RESPONSE",
            FormatException => "INVALID_RESPONSE",
            ArgumentException => "INVALID_REQUEST",
            _ => "UNKNOWN_ERROR"
        };
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
        else
        {
            var costPerCharacter = 0.00004m;
            return inputTokens * costPerCharacter;
        }
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
                   message.Contains("monthly") ||
                   message.Contains("free tier") ||
                   message.Contains("character");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}