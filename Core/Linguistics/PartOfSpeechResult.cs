namespace DictionaryImporter.Core.Linguistics
{
    public sealed class PartOfSpeechResult
    {
        public string Pos { get; init; } = "unk";
        public byte Confidence { get; init; }
    }
}