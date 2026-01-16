namespace DictionaryImporter.AITextKit.AI.Configuration;

public class FreeTierLimits
{
    public int MaxTokens { get; set; } = 1000;
    public int MaxRequestsPerDay { get; set; } = 100;
    public int MaxImagesPerMonth { get; set; } = 50;
    public int MaxAudioMinutesPerMonth { get; set; } = 60;
    public int MaxCharactersPerMonth { get; set; } = 10000;
}