using JsonIgnoreAttribute = System.Text.Json.Serialization.JsonIgnoreAttribute;

namespace DictionaryImporter.AITextKit.AI.Core.Models
{
    public class AiRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 1000;
        public string SystemPrompt { get; set; }
        public Dictionary<string, object> AdditionalParameters { get; set; }

        public RequestContext Context { get; set; } = new();

        [JsonIgnore]
        public byte[] AudioData { get; set; }

        [JsonIgnore]
        public byte[] ImageData { get; set; }

        public string AudioFormat { get; set; }
        public string ImageFormat { get; set; }
        public List<string> ImageUrls { get; set; }

        public bool NeedsImageGeneration { get; set; }

        public bool NeedsTextToSpeech { get; set; }
        public bool NeedsTranscription { get; set; }

        public RequestType Type { get; set; } = RequestType.TextCompletion;

        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}