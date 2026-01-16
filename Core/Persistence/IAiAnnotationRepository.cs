namespace DictionaryImporter.Core.Persistence
{
    public interface IAiAnnotationRepository
    {
        Task<IReadOnlyList<AiDefinitionCandidate>> GetDefinitionCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct);

        Task SaveAiEnhancementsAsync(
            string sourceCode,
            IReadOnlyList<AiDefinitionEnhancement> enhancements,
            CancellationToken ct);
    }
}