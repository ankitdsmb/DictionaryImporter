namespace DictionaryImporter.Core.Text;

public interface IOcrArtifactNormalizer
{
    string Normalize(string text, string languageCode = "en");
}