using NTextCat;

namespace DictionaryImporter.Core.Grammar;

public sealed class LanguageDetector : ILanguageDetector
{
    private readonly RankedLanguageIdentifier _identifier;

    public LanguageDetector()
    {
        var factory = new RankedLanguageIdentifierFactory();
        // We need to embed the profile or load from a file
        // Core14.profile.xml is available in the NTextCat package
        // We'll copy it to the output directory
        _identifier = factory.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core14.profile.xml"));
    }

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en"; // default

        var languages = _identifier.Identify(text);
        var mostCertainLanguage = languages.FirstOrDefault();
        if (mostCertainLanguage != null)
        {
            // Map ISO639-3 to LanguageTool language code
            return MapIso639ToLanguageCode(mostCertainLanguage.Item1.Iso639_3);
        }

        return "en";
    }

    private string MapIso639ToLanguageCode(string iso639)
    {
        // Map to LanguageTool language codes
        var mapping = new Dictionary<string, string>
        {
            { "eng", "en-US" },
            { "fra", "fr" },
            { "deu", "de" },
            // Add more as needed
        };

        return mapping.TryGetValue(iso639, out var code) ? code : "en-US";
    }
}

public interface ILanguageDetector
{
    string Detect(string text);
}