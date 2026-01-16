namespace DictionaryImporter.AITextKit.AI.Configuration
{
    public class ConfigurationValidator(ILogger<ConfigurationValidator> logger)
    {
        public ConfigurationValidationResult ValidateConfiguration(ProviderConfiguration config)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            if (string.IsNullOrWhiteSpace(config.Name))
                errors.Add("Provider name is required");

            if (config.IsEnabled)
            {
                if (string.IsNullOrWhiteSpace(config.ApiKey))
                    errors.Add("API key is required for enabled providers");

                if (!string.IsNullOrEmpty(config.ApiKey))
                {
                    if (config.ApiKey.StartsWith("${") && config.ApiKey.EndsWith("}"))
                        warnings.Add("API key appears to be an environment variable placeholder");

                    if (config.ApiKey.Length < 10)
                        warnings.Add("API key seems unusually short");
                }

                if (!string.IsNullOrEmpty(config.BaseUrl))
                {
                    if (!Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out _))
                        warnings.Add($"Base URL '{config.BaseUrl}' may not be a valid URI");
                }

                if (config.TimeoutSeconds <= 0)
                    errors.Add("Timeout must be greater than 0");
                else if (config.TimeoutSeconds > 300)
                    warnings.Add($"Timeout of {config.TimeoutSeconds} seconds is unusually high");

                if (config.MaxRetries < 0)
                    errors.Add("Max retries cannot be negative");
                else if (config.MaxRetries > 10)
                    warnings.Add($"Max retries of {config.MaxRetries} is unusually high");

                if (config.RequestsPerMinute <= 0)
                    warnings.Add("Requests per minute should be greater than 0");
                if (config.RequestsPerDay <= 0)
                    warnings.Add("Requests per day should be greater than 0");
            }

            return new ConfigurationValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors,
                Warnings = warnings
            };
        }

        public Dictionary<string, ConfigurationValidationResult> ValidateAllConfigurations(
            AiOrchestrationConfiguration config)
        {
            var results = new Dictionary<string, ConfigurationValidationResult>();

            foreach (var kvp in config.Providers)
            {
                var providerName = kvp.Key;
                var providerConfig = kvp.Value;

                try
                {
                    results[providerName] = ValidateConfiguration(providerConfig);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to validate configuration for provider {Provider}", providerName);
                    results[providerName] = new ConfigurationValidationResult
                    {
                        IsValid = false,
                        Errors = new List<string> { $"Validation failed: {ex.Message}" }
                    };
                }
            }

            return results;
        }
    }
}