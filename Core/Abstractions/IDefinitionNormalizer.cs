namespace DictionaryImporter.Core.Abstractions;

public interface IDefinitionNormalizer
{
    string Normalize(string raw);
}