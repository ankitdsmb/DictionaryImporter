using Newtonsoft.Json.Linq;

namespace DictionaryImporter.Gateway.Ai.Configuration;

public sealed class AiProviderConfig
{
    public string Name { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";

    // Example: "Authorization: Bearer {ApiKey}" OR "x-api-key: {ApiKey}"
    public string AuthHeader { get; init; } = "";

    // Optional extra headers, e.g. { "OpenAI-Organization": "xxx" }
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // Request JSON template (tokens will be replaced)
    public JObject RequestTemplate { get; init; } = new();

    // JSONPath for output text (supports Newtonsoft SelectToken paths, e.g. $.choices[0].message.content)
    public string ResponsePath { get; init; } = "";

    // Default model (used if request.Model is null/empty)
    public string DefaultModel { get; init; } = "";
}