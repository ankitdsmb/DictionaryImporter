using System.Text.Json.Serialization;

namespace DictionaryImporter.Sources.StructuredJson.Models;

internal sealed class StructuredJsonDefinition
{
    [JsonPropertyName("part_of_speech")] public string PartOfSpeech { get; set; } = null!;

    [JsonPropertyName("definition")] public string Definition { get; set; } = null!;

    [JsonPropertyName("sequence")] public int Sequence { get; set; }
}