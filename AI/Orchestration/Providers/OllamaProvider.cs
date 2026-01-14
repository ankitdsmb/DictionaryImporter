using DictionaryImporter.AI.Orchestration.Providers;
using Microsoft.Extensions.Configuration;

public class OllamaProvider : BaseCompletionProvider
{
    private const string DefaultModel = "llama2";
    private const int DefaultMaxTokens = 2000;

    public override string ProviderName => "Ollama";
    public override int Priority => 99;
    public override ProviderType Type => ProviderType.TextCompletion;

    public override bool SupportsAudio => false;

    public override bool SupportsVision => false;
    public override bool SupportsImages => false;
    public override bool SupportsTextToSpeech => false;
    public override bool SupportsTranscription => false;
    public override bool IsLocal => true;

    public OllamaProvider(
        HttpClient httpClient,
        ILogger<OllamaProvider> logger,
        IOptions<ProviderConfiguration> configuration)
        : base(httpClient, logger, configuration)
    {
        ConfigureAuthentication();
    }

    protected override void ConfigureCapabilities()
    {
        base.ConfigureCapabilities();
        Capabilities.TextCompletion = true;
        Capabilities.MaxTokensLimit = DefaultMaxTokens;
        Capabilities.SupportedLanguages.Add("en");
    }

    protected override void ConfigureAuthentication()
    {
        HttpClient.DefaultRequestHeaders.Clear();
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
            ValidateRequest(request);

            var payload = CreateRequestPayload(request);
            var httpRequest = CreateHttpRequest(payload);
            var model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model;

            Logger.LogDebug("Sending request to Ollama with model {Model}", model);

            var response = await SendWithResilienceAsync(
                () => HttpClient.SendAsync(httpRequest, cancellationToken),
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = ParseResponse(content);

            stopwatch.Stop();

            return new AiResponse
            {
                Content = result.Trim(),
                Provider = ProviderName,
                Model = model,
                TokensUsed = EstimateTokenUsage(request.Prompt, result),
                ProcessingTime = stopwatch.Elapsed,
                IsSuccess = true,
                Metadata = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "local", true },
                        { "offline_capable", true },
                        { "self_hosted", true }
                    }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Ollama provider failed");
            throw;
        }
    }

    private void ValidateRequest(AiRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt cannot be empty");
    }

    private object CreateRequestPayload(AiRequest request)
    {
        return new
        {
            model = string.IsNullOrEmpty(Configuration.Model) ? DefaultModel : Configuration.Model,
            prompt = request.Prompt,
            stream = false,
            options = new
            {
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                num_predict = Math.Min(request.MaxTokens, DefaultMaxTokens)
            }
        };
    }

    private HttpRequestMessage CreateHttpRequest(object payload)
    {
        var baseUrl = string.IsNullOrEmpty(Configuration.BaseUrl) ?
            "http://localhost:11434/api/generate" : Configuration.BaseUrl;

        return new HttpRequestMessage(HttpMethod.Post, baseUrl)
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
    }

    private string ParseResponse(string jsonResponse)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            if (jsonDoc.RootElement.TryGetProperty("response", out var response))
            {
                return response.GetString() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse Ollama response");
            throw new FormatException("Invalid Ollama response format");
        }
    }

    private long EstimateTokenUsage(string prompt, string response)
    {
        return (prompt.Length + response.Length) / 4;
    }

    public override bool ShouldFallback(Exception exception)
    {
        return false;
    }
}