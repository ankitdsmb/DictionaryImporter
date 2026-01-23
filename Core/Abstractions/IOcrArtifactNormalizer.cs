namespace DictionaryImporter.Core.Abstractions;

public interface IOcrArtifactNormalizer
{
    string Normalize(string text, string languageCode = "en");
}