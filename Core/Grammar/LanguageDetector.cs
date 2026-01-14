using NTextCat;

namespace DictionaryImporter.Core.Grammar;

public sealed class LanguageDetector : ILanguageDetector
{
    private readonly RankedLanguageIdentifier _identifier;

    public LanguageDetector()
    {
        var factory = new RankedLanguageIdentifierFactory();
        _identifier = factory.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core14.profile.xml"));
    }

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "en";

        var languages = _identifier.Identify(text);
        var mostCertainLanguage = languages.FirstOrDefault();
        if (mostCertainLanguage != null)
        {
            return MapIso639ToLanguageCode(mostCertainLanguage.Item1.Iso639_3);
        }

        return "en";
    }

    private string MapIso639ToLanguageCode(string iso639)
    {
        var mapping = new Dictionary<string, string>
        {
            { "eng", "en-US" },
            { "fra", "fr" },
            { "deu", "de" },
        };

        return mapping.TryGetValue(iso639, out var code) ? code : "en-US";
    }
}