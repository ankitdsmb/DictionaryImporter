namespace DictionaryImporter.Domain.Models
{
    public sealed class OcrNormalizationOptions
    {
        public bool Enabled { get; set; } = true;

        public bool EnableHunspellSplit { get; set; } = true;

        public bool EnableReplacements { get; set; } = true;

        public bool LogChanges { get; set; } = false;

        public int MinTokenLengthForSplit { get; set; } = 6;

        public Dictionary<string, string> Replacements { get; set; } = new();
    }
}