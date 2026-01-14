namespace DictionaryImporter.AI.Core.Contracts;

public interface ITextToSpeechProvider : ICompletionProvider
{
    Task<byte[]> GenerateSpeechAsync(
        string text,
        VoiceOptions options,
        CancellationToken cancellationToken = default);
}