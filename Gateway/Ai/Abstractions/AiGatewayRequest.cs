namespace DictionaryImporter.Gateway.Ai.Abstractions
{
    public sealed class AiGatewayRequest
    {
        public AiTaskType Task { get; init; }
        public AiCapability Capability { get; init; }

        // Text/JSON inputs
        public string? InputText { get; init; }

        // Binary inputs (image/audio/video)
        public byte[]? InputBytes { get; init; }

        public string? InputMimeType { get; init; }

        // Optional metadata
        public string? SourceCode { get; init; }

        public string? Language { get; init; } = "en";
        public string? CorrelationId { get; init; }

        // Execution settings
        public AiExecutionOptions Options { get; init; } = new();

        // Provider routing
        public string? ProviderName { get; init; }

        public string? Model { get; init; }

        // Prompts
        public string? SystemPrompt { get; init; } = "You are a helpful assistant.";

        public string? Prompt { get; init; }

        // Any extra variables for templates
        public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}