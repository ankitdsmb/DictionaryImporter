namespace DictionaryImporter.Core.Linguistics;

public interface IOrthographicSyllableRule
{
    string LocaleCode { get; }

    IReadOnlyList<string> Apply(
        IReadOnlyList<string> syllables,
        string word);
}