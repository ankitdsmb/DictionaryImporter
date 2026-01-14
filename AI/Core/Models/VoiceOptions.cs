namespace DictionaryImporter.AI.Core.Models;

public class VoiceOptions
{
    public string VoiceId { get; set; } = "21m00Tcm4TlvDq8ikWAM";
    public string Model { get; set; } = "eleven_monolingual_v1";
    public double Stability { get; set; } = 0.5;
    public double SimilarityBoost { get; set; } = 0.5;
    public double Style { get; set; } = 0.0;
    public bool UseSpeakerBoost { get; set; } = true;
    public double Speed { get; set; } = 1.0;
    public string Language { get; set; } = "en";

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            { "voice_id", VoiceId },
            { "model", Model },
            { "stability", Stability },
            { "similarity_boost", SimilarityBoost },
            { "style", Style },
            { "use_speaker_boost", UseSpeakerBoost },
            { "speed", Speed },
            { "language", Language }
        };
    }
}