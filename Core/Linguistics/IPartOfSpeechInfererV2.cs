using DictionaryImporter.Core.Domain.Models;

namespace DictionaryImporter.Core.Linguistics;

public interface IPartOfSpeechInfererV2
{
    PartOfSpeechResult InferWithConfidence(string definition);
}