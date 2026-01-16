namespace DictionaryImporter.Core.Text;

public sealed class DictionaryTextFormattingOptions
{
    public string Style { get; set; } = "Modern";

    public int MaxDefinitionLineLength { get; set; } = 120;

    public bool UseBulletsForMultiLineDefinitions { get; set; } = true;

    public bool KeepSemicolons { get; set; } = true;

    public bool TitleCaseMeaningTitle { get; set; } = true;
}