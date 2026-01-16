namespace DictionaryImporter.AITextKit.AI.Configuration;

public class AiValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Summary => IsValid ? "Valid" : $"Invalid: {string.Join("; ", Errors)}";
}