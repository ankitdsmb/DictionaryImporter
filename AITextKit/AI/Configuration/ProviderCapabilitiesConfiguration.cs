namespace DictionaryImporter.AITextKit.AI.Configuration;

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