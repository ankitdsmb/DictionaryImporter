using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Core.Abstractions
{
    public interface IAiAnnotationRepository
    {
        Task<IReadOnlyList<AiDefinitionCandidate>> GetDefinitionCandidatesAsync(
            string sourceCode,
            int take,
            CancellationToken ct);

        Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> GetExamplesByParsedIdsAsync(
            string sourceCode,
            IReadOnlyList<long> parsedDefinitionIds,
            int maxExamplesPerParsedId,
            CancellationToken ct);

        Task<IReadOnlySet<long>> GetAlreadyEnhancedParsedIdsAsync(
            string sourceCode,
            IReadOnlyList<long> parsedDefinitionIds,
            string provider,
            string model,
            CancellationToken ct);

        Task SaveAiEnhancementsAsync(
            string sourceCode,
            IReadOnlyList<AiDefinitionEnhancement> enhancements,
            CancellationToken ct);

        Task<string?> GetAiNotesJsonAsync(
            string sourceCode,
            long parsedDefinitionId,
            string provider,
            string model,
            CancellationToken cancellationToken);

        Task UpdateAiNotesJsonAsync(
            string sourceCode,
            long parsedDefinitionId,
            string provider,
            string model,
            string aiNotesJson,
            CancellationToken cancellationToken);

        Task<string?> GetOriginalDefinitionAsync(
            string sourceCode,
            long parsedDefinitionId,
            CancellationToken cancellationToken);
    }
}