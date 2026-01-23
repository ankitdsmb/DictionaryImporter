namespace DictionaryImporter.Domain.Models
{
    public sealed class DictionaryTextFormattingOptions
    {
        public string Style { get; set; } = "Modern";

        public int MaxDefinitionLineLength { get; set; } = int.MaxValue;

        public bool UseBulletsForMultiLineDefinitions { get; set; } = true;

        public bool KeepSemicolons { get; set; } = true;

        public bool TitleCaseMeaningTitle { get; set; } = true;
    }
}