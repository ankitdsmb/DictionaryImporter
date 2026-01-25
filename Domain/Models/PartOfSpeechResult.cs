namespace DictionaryImporter.Domain.Models;

public sealed class PartOfSpeechResult
{
    public string Pos { get; init; } = "unk";
    public byte Confidence { get; init; }
}