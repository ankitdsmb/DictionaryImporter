using Humanizer;

namespace DictionaryImporter.Core.Text.Experimental;

public class DefinitionNormalizerWithHumanizer : IDefinitionNormalizer
{
    public string Normalize(string raw)
    {
        // Capitalization, pluralization, number formatting
        var text = raw.Transform(To.LowerCase, To.TitleCase);
        text = text.Dehumanize(); // Removes human-readable formatting
        text = text.Humanize(); // Makes it human-readable

        return text;
    }
}