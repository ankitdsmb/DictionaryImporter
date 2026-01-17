namespace DictionaryImporter.Core.Text
{
    public interface IDictionaryTextFormatter
    {
        string FormatDefinition(string raw);

        string FormatExample(string raw);

        string? FormatSynonym(string raw);

        string? FormatAntonym(string raw);

        string FormatEtymology(string raw);
    }
}