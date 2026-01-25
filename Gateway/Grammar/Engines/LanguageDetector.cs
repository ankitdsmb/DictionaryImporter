using DictionaryImporter.Gateway.Grammar.Core;
using NTextCat;

namespace DictionaryImporter.Gateway.Grammar.Engines;

public sealed class LanguageDetector : ILanguageDetector
{
    private readonly RankedLanguageIdentifier _identifier;

    public LanguageDetector()
    {
        try
        {
            // File must exist in output folder
            var profilePath = Path.Combine(
                AppContext.BaseDirectory,
                "Gateway",
                "Grammar",
                "Configuration",
                "Core14.profile.xml"
            );

            if (!File.Exists(profilePath))
                throw new FileNotFoundException($"NTextCat profile not found: {profilePath}");

            var factory = new RankedLanguageIdentifierFactory();
            _identifier = factory.Load(profilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"NTextCat load failed. Falling back to en-US. Error: {ex.Message}");
            _identifier = null;
        }
    }

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 10)
            return "en-US";

        if (_identifier == null)
            return "en-US";

        var best = _identifier.Identify(text).FirstOrDefault();
        var iso639_3 = best?.Item1?.Iso639_3;

        return iso639_3 switch
        {
            "eng" => "en-US",
            "fra" => "fr-FR",
            "deu" => "de-DE",
            "spa" => "es-ES",
            "ita" => "it-IT",
            _ => "en-US"
        };
    }
}