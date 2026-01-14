namespace DictionaryImporter.AI.Core.Contracts;

public interface IImageGenerationProvider : ICompletionProvider
{
    Task<AiResponse> GenerateImageAsync(
        string prompt,
        ImageGenerationOptions options,
        CancellationToken cancellationToken = default);
}