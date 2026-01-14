using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class ElevenLabsProvider : BaseCompletionProvider
{
    private const string DefaultVoice = "21m00Tcm4TlvDq8ikWAM";
    private const int FreeTierMaxCharacters = 10000;
    private const int FreeTierMonthlyCharacters = 10000;

    private static long _monthlyCharacterCount = 0;
    private static DateTime _lastResetMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    private static readonly object MonthlyCounterLock = new();

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
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("ElevenLabs API key not configured. Provider will be disabled.");
            return;
        }
        ConfigureAuthentication();
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextToSpeech = true;
        Capabilities.SupportedAudioFormats.Add("mp3");
        Capabilities.SupportedAudioFormats.Add("wav");
        Capabilities.SupportedLanguages.Add("en");
    }

    protected override void ConfigureAuthentication()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("xi-api-key", Configuration.ApiKey);
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
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                throw new InvalidOperationException("ElevenLabs API key not configured");
            }

            if (!CheckMonthlyLimit(request.Prompt.Length))
            {
                throw new ElevenLabsQuotaExceededException(
                    $"ElevenLabs free tier monthly limit reached: {FreeTierMonthlyCharacters} characters/month");
            }

            ValidateSpeechRequest(request);
            IncrementMonthlyUsage(request.Prompt.Length);

            var audioData = await GenerateSpeechAsync(request, cancellationToken);

            stopwatch.Stop();

            return new AiResponse
            {
                Content = Convert.ToBase64String(audioData),
                Provider = ProviderName,
                TokensUsed = request.Prompt.Length,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                AudioData = audioData,
                AudioFormat = "mp3",
                Metadata = new Dictionary<string, object>
                    {
                        { "voice", Configuration.Model },
                        { "free_tier", true },
                        { "monthly_characters_used", GetMonthlyCharacterCount() },
                        { "monthly_characters_remaining", FreeTierMonthlyCharacters - GetMonthlyCharacterCount() },
                        { "audio_format", "mp3" },
                        { "content_type", "audio/mpeg" },
                        { "voice_name", GetVoiceName(Configuration.Model) }
                    }
            };
        }
        catch (ElevenLabsQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "ElevenLabs provider failed");
            if (ShouldFallback(ex)) throw;

            return new AiResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                    {
                        { "voice", string.IsNullOrEmpty(Configuration.Model) ? DefaultVoice : Configuration.Model },
                        { "error_type", ex.GetType().Name }
                    }
            };
        }
    }

    private bool CheckMonthlyLimit(int additionalCharacters)
    {
        lock (MonthlyCounterLock)
        {
            var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (currentMonth > _lastResetMonth)
            {
                _monthlyCharacterCount = 0;
                _lastResetMonth = currentMonth;
            }
            return (_monthlyCharacterCount + additionalCharacters) <= FreeTierMonthlyCharacters;
        }
    }

    private void IncrementMonthlyUsage(int characters)
    {
        lock (MonthlyCounterLock)
        {
            _monthlyCharacterCount += characters;
        }
    }

    private long GetMonthlyCharacterCount()
    {
        lock (MonthlyCounterLock)
        {
            var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (currentMonth > _lastResetMonth)
            {
                _monthlyCharacterCount = 0;
                _lastResetMonth = currentMonth;
            }
            return _monthlyCharacterCount;
        }
    }

    private void ValidateSpeechRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Speech text cannot be empty");

        if (request.Prompt.Length > FreeTierMaxCharacters)
            throw new ArgumentException($"Text too long for free tier (max {FreeTierMaxCharacters} characters)");
    }

    private async Task<byte[]> GenerateSpeechAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var voiceId = string.IsNullOrEmpty(Configuration.Model) ? DefaultVoice : Configuration.Model;
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "https://api.elevenlabs.io/v1/text-to-speech" : Configuration.BaseUrl;

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
                JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }),
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

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is ElevenLabsQuotaExceededException)
            return true;

        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("monthly") ||
                   message.Contains("free tier") ||
                   message.Contains("character");
        }

        return base.ShouldFallback(exception);
    }
}