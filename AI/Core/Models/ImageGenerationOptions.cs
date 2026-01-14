namespace DictionaryImporter.AI.Core.Models;

public class ImageGenerationOptions
{
    public int Width { get; set; } = 512;
    public int Height { get; set; } = 512;
    public string Style { get; set; } = "realistic";
    public int Steps { get; set; } = 30;
    public double GuidanceScale { get; set; } = 7.5;
    public string NegativePrompt { get; set; } = string.Empty;
    public int Seed { get; set; } = new Random().Next();
    public string OutputFormat { get; set; } = "png";

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "width", Width },
            { "height", Height },
            { "style", Style },
            { "steps", Steps },
            { "guidance_scale", GuidanceScale },
            { "negative_prompt", NegativePrompt },
            { "seed", Seed },
            { "output_format", OutputFormat }
        };
    }
}