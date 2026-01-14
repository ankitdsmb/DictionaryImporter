namespace DictionaryImporter.AI.Core.Models;

public class TranscriptionOptions
{
    public string Language { get; set; } = "en";
    public bool Punctuate { get; set; } = true;
    public bool FormatText { get; set; } = true;
    public bool Diarize { get; set; } = false;
    public bool DetectLanguage { get; set; } = true;
    public string Model { get; set; } = "enhanced";

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "language_code", Language },
            { "punctuate", Punctuate },
            { "format_text", FormatText },
            { "diarize", Diarize },
            { "detect_language", DetectLanguage },
            { "model", Model }
        };
    }
}