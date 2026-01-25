namespace DictionaryImporter.Gateway.Ai.Abstractions;

public enum AiTaskType
{
    Generic,

    GrammarFix,
    RewriteDefinition,
    Summarize,
    Translate,
    ExtractKeywords,
    Classification,
    GenerateExampleSentences,
    ValidateMeaning,

    ImageGenerate,
    AudioTranscribe,
    AudioSpeak
}