namespace DictionaryImporter.AI.Configuration
{
    public class ProviderConfiguration
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 2;
        public bool IsEnabled { get; set; } = true;

        public int CircuitBreakerFailuresBeforeBreaking { get; set; } = 5;

        public int CircuitBreakerDurationSeconds { get; set; } = 30;

        public int RequestsPerMinute { get; set; } = 60;

        public int RequestsPerDay { get; set; } = 1000;

        public FreeTierLimits FreeTier { get; set; } = new();

        public Dictionary<string, string> AdditionalSettings { get; set; } = new();
    }
}