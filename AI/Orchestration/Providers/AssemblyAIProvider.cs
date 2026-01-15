using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Core.Models;
using DictionaryImporter.AI.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace DictionaryImporter.AI.Orchestration.Providers;

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
            if (!Configuration.IsEnabled)
            {
                throw new InvalidOperationException("AssemblyAI provider is disabled");
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

            var (audioInput, estimatedDuration) = await GetAudioInputAsync(request, cancellationToken);

            var transcriptId = await SubmitTranscriptionAsync(audioInput, cancellationToken);

            var result = await PollForTranscriptionAsync(transcriptId, cancellationToken);
            stopwatch.Stop();

            var tokenUsage = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            var aiResponse = new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                TokensUsed = tokenUsage,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                EstimatedCost = EstimateCost(tokenUsage, 0),
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
                    ["tokens_used"] = tokenUsage,
                    ["estimated_cost"] = EstimateCost(tokenUsage, 0),
                    ["assemblyai"] = true,
                    ["audio_transcription"] = true,
                    ["estimated_audio_duration_seconds"] = estimatedDuration,
                    ["transcript_id"] = transcriptId,
                    ["audio_format"] = GetAudioFormat(request),
                    ["processing_time_seconds"] = stopwatch.Elapsed.TotalSeconds
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
            Logger.LogError(ex, "AssemblyAI provider failed for request {RequestId}", request.Context?.RequestId);

            if (ShouldFallback(ex))
            {
                throw;
            }

            var errorResponse = new AiResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                Model = Configuration.Model ?? DefaultModel,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = false,
                ErrorCode = GetErrorCode(ex),
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    ["model"] = Configuration.Model ?? DefaultModel,
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

    private async Task<(string AudioInput, int EstimatedDuration)> GetAudioInputAsync(
        AiRequest request,
        CancellationToken cancellationToken)
    {
        string audioInput;
        int estimatedDuration;

        if (IsAudioUrl(request.Prompt))
        {
            audioInput = request.Prompt;
            estimatedDuration = 60;
        }
        else if (IsAudioBase64(request.Prompt))
        {
            audioInput = request.Prompt;
            estimatedDuration = EstimateAudioDurationFromBase64(request.Prompt);
        }
        else if (request.AudioData != null && request.AudioData.Length > 0)
        {
            audioInput = Convert.ToBase64String(request.AudioData);
            estimatedDuration = EstimateAudioDurationFromBytes(request.AudioData);
        }
        else
        {
            throw new ArgumentException("Audio URL, base64 data, or AudioData required for AssemblyAI transcription");
        }

        return (audioInput, estimatedDuration);
    }

    private bool IsAudioUrl(string input)
    {
        return !string.IsNullOrEmpty(input) &&
               (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAudioBase64(string input)
    {
        return !string.IsNullOrEmpty(input) &&
               (input.StartsWith("data:audio/", StringComparison.OrdinalIgnoreCase) ||
                (input.Length > 100 && input.Contains("base64")));
    }

    private int EstimateAudioDurationFromBase64(string base64Data)
    {
        try
        {
            string base64String;
            if (base64Data.StartsWith("data:audio/"))
            {
                var parts = base64Data.Split(',');
                if (parts.Length < 2) return 60;
                base64String = parts[1];
            }
            else
            {
                base64String = base64Data;
            }

            var dataLength = base64String.Length * 3 / 4;
            var kilobytes = dataLength / 1024.0;
            var minutes = kilobytes / 1024.0;
            return (int)Math.Max(1, minutes * 60);
        }
        catch
        {
            return 60;
        }
    }

    private int EstimateAudioDurationFromBytes(byte[] audioData)
    {
        try
        {
            var megabytes = audioData.Length / (1024.0 * 1024.0);
            return (int)Math.Max(1, megabytes * 60);
        }
        catch
        {
            return 60;
        }
    }

    private async Task<string> SubmitTranscriptionAsync(string audioInput, CancellationToken cancellationToken)
    {
        var payload = CreateTranscriptionPayload(audioInput);
        var httpRequest = CreateHttpRequest(payload);

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

        if (IsAudioUrl(audioInput))
        {
            payload["audio_url"] = audioInput;
        }
        else if (IsAudioBase64(audioInput))
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

        foreach (var setting in Configuration.AdditionalSettings.Where(setting => !payload.ContainsKey(setting.Key)))
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

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var url = Configuration.BaseUrl ?? BaseUrl;

        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonSerializerOptions),
                Encoding.UTF8,
                "application/json")
        };
    }

    private string GetAudioFormat(AiRequest request)
    {
        if (!string.IsNullOrEmpty(request.AudioFormat))
            return request.AudioFormat;

        if (IsAudioUrl(request.Prompt))
        {
            var url = request.Prompt.ToLower();
            if (url.Contains(".mp3")) return "mp3";
            if (url.Contains(".wav")) return "wav";
            if (url.Contains(".flac")) return "flac";
            if (url.Contains(".m4a")) return "m4a";
        }

        if (IsAudioBase64(request.Prompt))
        {
            if (request.Prompt.Contains("data:audio/mp3")) return "mp3";
            if (request.Prompt.Contains("data:audio/wav")) return "wav";
            if (request.Prompt.Contains("data:audio/flac")) return "flac";
            if (request.Prompt.Contains("data:audio/m4a")) return "m4a";
        }

        return "unknown";
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
                   message.Contains("free tier");
        }

        if (exception is TimeoutException || exception is TaskCanceledException)
            return true;

        return base.ShouldFallback(exception);
    }
}