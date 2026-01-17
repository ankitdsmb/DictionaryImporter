namespace DictionaryImporter.Gateway.Ai.Abstractions
{
    public sealed class AiProviderResult
    {
        public string Provider { get; init; }
        public string Model { get; init; }

        public bool Success { get; init; }

        public string? Text { get; init; }

        public byte[]? Bytes { get; init; }
        public string? MimeType { get; init; }

        public int? InputTokens { get; init; }
        public int? OutputTokens { get; init; }

        public long DurationMs { get; init; }

        public string? Error { get; init; }
    }
}