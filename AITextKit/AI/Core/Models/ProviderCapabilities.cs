namespace DictionaryImporter.AITextKit.AI.Core.Models;

public class ProviderCapabilities
{
    public bool TextCompletion { get; set; }
    public bool ChatCompletion { get; set; }
    public bool ImageGeneration { get; set; }
    public bool ImageAnalysis { get; set; }
    public bool AudioTranscription { get; set; }
    public bool TextToSpeech { get; set; }
    public bool CodeGeneration { get; set; }
    public bool Translation { get; set; }
    public bool Summarization { get; set; }
    public List<string> SupportedImageFormats { get; set; } = new();
    public List<string> SupportedAudioFormats { get; set; } = new();
    public List<string> SupportedLanguages { get; set; } = new();
    public int MaxTokensLimit { get; set; } = 4096;
    public int MaxImageSize { get; set; } = 1024;
}