namespace DictionaryImporter.AI.Core.Contracts
{
    public interface IAudioTranscriptionProvider : ICompletionProvider
    {
        Task<AiResponse> TranscribeAudioAsync(
            byte[] audioData,
            string audioFormat,
            CancellationToken cancellationToken = default);
    }
}