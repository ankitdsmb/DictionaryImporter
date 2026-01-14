namespace DictionaryImporter.AI.Core.Models;

public class VisionAnalysisOptions
{
    public int MaxTokens { get; set; } = 1000;
    public double Temperature { get; set; } = 0.3;
    public string Task { get; set; } = "describe";
    public bool Detailed { get; set; } = true;
    public List<string> Languages { get; set; } = new() { "en" };

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "max_tokens", MaxTokens },
            { "temperature", Temperature },
            { "task", Task },
            { "detailed", Detailed },
            { "languages", Languages }
        };
    }
}