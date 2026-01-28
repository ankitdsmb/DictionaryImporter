using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Core.Abstractions;

public interface IExampleAiEnhancementRepository
{
    // ✅ Updated to include forceRewrite (default false behavior preserved by callers)
    Task<IReadOnlyList<ExampleRewriteCandidate>> GetExampleCandidatesAsync(
        string sourceCode,
        int take,
        bool forceRewrite,
        CancellationToken ct);

    // ✅ Updated to include forceRewrite
    Task SaveExampleEnhancementsAsync(
        string sourceCode,
        IReadOnlyList<ExampleRewriteEnhancement> enhancements,
        bool forceRewrite,
        CancellationToken ct);
}