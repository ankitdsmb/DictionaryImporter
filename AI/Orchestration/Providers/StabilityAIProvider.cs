using DictionaryImporter.AI.Core.Exceptions;
using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class StabilityAiProvider : BaseCompletionProvider
{
    private const string DefaultModel = "stable-diffusion-xl-1024-v1-0";
    private const int FreeTierMaxImages = 100;
    private const int FreeTierImageSize = 512;

    private static long _monthlyImageCount = 0;
    private static DateTime _lastResetMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    private static readonly object MonthlyCounterLock = new();

    public override string ProviderName => "StabilityAI";
    public override int Priority => 14;
    public override ProviderType Type => ProviderType.ImageGeneration;

    public override bool SupportsAudio => false;

    public override bool SupportsVision => false;
    public override bool SupportsImages => true;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => false;

    public StabilityAiProvider(
        HttpClient httpClient,
        ILogger<StabilityAiProvider> logger,
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        if (string.IsNullOrEmpty(Configuration.ApiKey))
        {
            Logger.LogWarning("Stability AI API key not configured. Provider will be disabled.");
            return;
        }
        ConfigureAuthentication();
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.ImageGeneration = true;
        Capabilities.MaxImageSize = FreeTierImageSize;
        Capabilities.SupportedImageFormats.Add("png");
        Capabilities.SupportedImageFormats.Add("jpeg");
    }

    protected override void ConfigureAuthentication()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {Configuration.ApiKey}");
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
            if (string.IsNullOrEmpty(Configuration.ApiKey))
            {
                throw new InvalidOperationException("Stability AI API key not configured");
            }

            if (!CheckMonthlyLimit())
            {
                throw new StabilityAiQuotaExceededException(
                    $"Stability AI free tier monthly limit reached: {FreeTierMaxImages} images/month");
            }

            ValidateImageRequest(request);
            IncrementMonthlyCount();

            var imageData = await GenerateImageAsync(request, cancellationToken);

            stopwatch.Stop();

            return new AiResponse
            {
                Content = Convert.ToBase64String(imageData),
                Provider = ProviderName,
                TokensUsed = EstimateTokenUsage(request.Prompt),
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                ImageData = imageData,
                ImageFormat = "png",
                Metadata = new Dictionary<string, object>
                    {
                        { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                        { "free_tier", true },
                        { "monthly_images_generated", GetMonthlyImageCount() },
                        { "monthly_images_remaining", FreeTierMaxImages - GetMonthlyImageCount() },
                        { "image_size", FreeTierImageSize },
                        { "image_format", "png" },
                        { "content_type", "image/png" }
                    }
            };
        }
        catch (StabilityAiQuotaExceededException ex)
        {
            stopwatch.Stop();
            Logger.LogWarning(ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Stability AI provider failed");
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
                        { "model", string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model },
                        { "error_type", ex.GetType().Name }
                    }
            };
        }
    }

    private bool CheckMonthlyLimit()
    {
        lock (MonthlyCounterLock)
        {
            var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (currentMonth > _lastResetMonth)
            {
                _monthlyImageCount = 0;
                _lastResetMonth = currentMonth;
            }
            return _monthlyImageCount < FreeTierMaxImages;
        }
    }

    private void IncrementMonthlyCount()
    {
        lock (MonthlyCounterLock)
        {
            _monthlyImageCount++;
        }
    }

    private long GetMonthlyImageCount()
    {
        lock (MonthlyCounterLock)
        {
            var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (currentMonth > _lastResetMonth)
            {
                _monthlyImageCount = 0;
                _lastResetMonth = currentMonth;
            }
            return _monthlyImageCount;
        }
    }

    private void ValidateImageRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Image prompt cannot be empty");

        if (request.Prompt.Length > 1000)
            throw new ArgumentException("Image prompt too long (max 1000 characters)");
    }

    private async Task<byte[]> GenerateImageAsync(AiRequest request, CancellationToken cancellationToken)
    {
        var payload = new
        {
            text_prompts = new[]
            {
                    new { text = request.Prompt, weight = 1.0 }
                },
            cfg_scale = 7,
            height = FreeTierImageSize,
            width = FreeTierImageSize,
            samples = 1,
            steps = 30
        };

        var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            $"https://api.stability.ai/v1/generation/{model}/text-to-image" : Configuration.BaseUrl;

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl)
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

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDoc = JsonDocument.Parse(content);

        if (jsonDoc.RootElement.TryGetProperty("artifacts", out var artifacts))
        {
            var firstImage = artifacts.EnumerateArray().FirstOrDefault();
            if (firstImage.TryGetProperty("base64", out var base64Element))
            {
                return Convert.FromBase64String(base64Element.GetString() ?? string.Empty);
            }
        }

        throw new InvalidOperationException("No image generated");
    }

    private long EstimateTokenUsage(string prompt)
    {
        return prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public override bool ShouldFallback(Exception exception)
    {
        if (exception is StabilityAiQuotaExceededException)
            return true;

        if (exception is HttpRequestException httpEx)
        {
            var message = httpEx.Message.ToLowerInvariant();
            return message.Contains("429") ||
                   message.Contains("quota") ||
                   message.Contains("limit") ||
                   message.Contains("monthly") ||
                   message.Contains("free tier");
        }

        return base.ShouldFallback(exception);
    }
}