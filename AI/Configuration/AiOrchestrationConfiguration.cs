using System.Text.Json;
using System.Text.Json.Serialization;

namespace DictionaryImporter.AI.Configuration;

public class AiOrchestrationConfiguration
{
    public bool EnableOrchestration { get; set; } = true;
    public bool EnableFallback { get; set; } = true;
    public bool EnableCaching { get; set; } = true;
    public int CacheDurationMinutes { get; set; } = 60;
    public bool EnableRateLimiting { get; set; } = true;
    public bool EnableQuotaManagement { get; set; } = true;
    public bool EnableAuditLogging { get; set; } = true;
    public bool EnableMetricsCollection { get; set; } = true;
    public int MaxConcurrentRequests { get; set; } = 20;
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int HealthCheckIntervalSeconds { get; set; } = 60;
    public string DefaultProvider { get; set; } = "OpenRouter";
    public int FailureHistorySize { get; set; } = 100;
    public List<string> FallbackOrder { get; set; } = new();
    public Dictionary<string, ProviderConfiguration> Providers { get; set; } = new();
}

public class ProviderCapabilitiesConfiguration
{
    public bool TextCompletion { get; set; } = true;
    public bool ChatCompletion { get; set; } = false;
    public bool ImageGeneration { get; set; } = false;
    public bool ImageAnalysis { get; set; } = false;
    public bool AudioTranscription { get; set; } = false;
    public bool TextToSpeech { get; set; } = false;
    public List<string> SupportedLanguages { get; set; } = new() { "en" };
    public List<string> SupportedImageFormats { get; set; } = new();
    public List<string> SupportedAudioFormats { get; set; } = new();
    public int MaxTokensLimit { get; set; } = 4096;
    public int MaxImageSize { get; set; } = 1024;
}

public class DatabaseConfiguration
{
    public string ConnectionString { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 10;
    public bool EnableRetryOnFailure { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 1;
    public int ConnectionLifetimeMinutes { get; set; } = 30;
    public bool EnableConnectionPooling { get; set; } = true;
    public int ConnectionIdleTimeoutSeconds { get; set; } = 300;
}

public class CacheConfiguration
{
    public string Type { get; set; } = "Memory";
    public string ConnectionString { get; set; }
    public int DefaultExpirationMinutes { get; set; } = 60;
    public int MaxCacheSizeMB { get; set; } = 100;
    public bool EnableCompression { get; set; } = true;
}

public class TelemetryConfiguration
{
    public bool EnableTelemetry { get; set; } = true;
    public string Provider { get; set; } = "ApplicationInsights";
    public string ConnectionString { get; set; }
    public string InstrumentationKey { get; set; }
    public int MetricsExportIntervalSeconds { get; set; } = 30;
    public string LogLevel { get; set; } = "Information";
    public bool EnableRequestTracking { get; set; } = true;
    public bool EnableDependencyTracking { get; set; } = true;
    public bool EnablePerformanceCounters { get; set; } = true;
}

public class SecurityConfiguration
{
    public bool EnableApiKeyRotation { get; set; } = false;
    public int ApiKeyRotationDays { get; set; } = 30;
    public string KeyVaultUrl { get; set; }
    public bool EnableContentValidation { get; set; } = true;
    public List<string> BlockedPatterns { get; set; } = new();
    public bool EnableUserQuotas { get; set; } = true;
    public int DefaultUserRequestsPerDay { get; set; } = 100;
    public long DefaultUserTokensPerDay { get; set; } = 100000;
}

public class AiConfigurationBuilder
{
    public static AiOrchestrationConfiguration BuildDefaultConfiguration()
    {
        return new AiOrchestrationConfiguration
        {
            EnableOrchestration = true,
            EnableFallback = true,
            EnableCaching = true,
            CacheDurationMinutes = 60,
            EnableRateLimiting = true,
            EnableQuotaManagement = true,
            EnableAuditLogging = true,
            EnableMetricsCollection = true,
            MaxConcurrentRequests = 20,
            DefaultTimeoutSeconds = 30,
            DefaultProvider = "OpenRouter",
            FallbackOrder = new List<string>
            {
                "OpenRouter",
                "Gemini",
                "Anthropic",
                "TogetherAI",
                "Cohere",
                "AI21",
                "Perplexity",
                "NLPCloud",
                "HuggingFace",
                "DeepAI",
                "Watson",
                "AlephAlpha",
                "Replicate",
                "Ollama",
                "StabilityAI",
                "ElevenLabs",
                "AssemblyAI"
            },
            Providers = new Dictionary<string, ProviderConfiguration>
            {
                ["OpenRouter"] = new ProviderConfiguration
                {
                    Name = "OpenRouter",
                    IsEnabled = true,
                    ApiKey = "${OPENROUTER_API_KEY}",
                    BaseUrl = "https://api.openrouter.ai/api/v1/chat/completions",
                    Model = "openai/gpt-3.5-turbo",
                    Priority = 1,
                    TimeoutSeconds = 30,
                    MaxRetries = 3,
                    EnableCaching = true,
                    CacheDurationMinutes = 60,
                    EnableRateLimiting = true,
                    RequestsPerMinute = 60,
                    RequestsPerDay = 1000,
                    TokensPerMinute = 90000,
                    TokensPerDay = 1000000,
                    CostLimitPerDay = 10.00m,
                    Capabilities = new ProviderCapabilitiesConfiguration
                    {
                        TextCompletion = true,
                        ChatCompletion = true,
                        SupportedLanguages = new List<string> { "en", "es", "fr", "de", "it", "ja", "ko", "zh" },
                        MaxTokensLimit = 4096
                    }
                },
                ["Gemini"] = new ProviderConfiguration
                {
                    Name = "Gemini",
                    IsEnabled = true,
                    ApiKey = "${GEMINI_API_KEY}",
                    BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                    Model = "gemini-pro",
                    Priority = 2,
                    TimeoutSeconds = 30,
                    MaxRetries = 3,
                    EnableCaching = true,
                    CacheDurationMinutes = 60,
                    EnableRateLimiting = true,
                    RequestsPerMinute = 60,
                    RequestsPerDay = 1000,
                    Capabilities = new ProviderCapabilitiesConfiguration
                    {
                        TextCompletion = true,
                        ChatCompletion = true,
                        ImageAnalysis = true,
                        SupportedLanguages = new List<string> { "en", "es", "fr", "de", "it", "ja", "ko", "zh" },
                        MaxTokensLimit = 32768
                    }
                }
            }
        };
    }

    public static AiOrchestrationConfiguration LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        var config = JsonSerializer.Deserialize<AiOrchestrationConfiguration>(json, options);

        ResolveEnvironmentVariables(config);

        return config;
    }

    private static void ResolveEnvironmentVariables(AiOrchestrationConfiguration config)
    {
        foreach (var provider in config.Providers.Values)
        {
            provider.ApiKey = ResolveEnvVar(provider.ApiKey);
            provider.BaseUrl = ResolveEnvVar(provider.BaseUrl);

            var resolvedSettings = new Dictionary<string, string>();
            foreach (var setting in provider.AdditionalSettings)
            {
                resolvedSettings[setting.Key] = ResolveEnvVar(setting.Value);
            }
            provider.AdditionalSettings = resolvedSettings;
        }
    }

    private static string ResolveEnvVar(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("${") || !value.EndsWith("}"))
            return value;

        var envVarName = value.Substring(2, value.Length - 3);
        return Environment.GetEnvironmentVariable(envVarName) ?? value;
    }
}