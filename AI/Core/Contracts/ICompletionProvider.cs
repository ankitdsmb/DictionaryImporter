namespace DictionaryImporter.AI.Core.Contracts
{
    public interface ICompletionProvider
    {
        string ProviderName { get; }
        int Priority { get; }
        bool IsEnabled { get; }
        bool SupportsAudio { get; }
        bool SupportsVision { get; }
        bool SupportsImages { get; }
        bool SupportsTextToSpeech { get; }
        bool SupportsTranscription { get; }
        bool IsLocal { get; }
        ProviderCapabilities Capabilities { get; }

        Task<AiResponse> GetCompletionAsync(
            AiRequest request,
            CancellationToken cancellationToken = default);

        bool CanHandleRequest(AiRequest request);

        bool ShouldFallback(Exception exception);
    }
}