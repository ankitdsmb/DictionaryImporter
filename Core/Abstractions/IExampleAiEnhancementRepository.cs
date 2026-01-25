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

public sealed class ExampleRewriteCandidate
{
    public long DictionaryEntryExampleId { get; set; }
    public long DictionaryEntryParsedId { get; set; }
    public string ExampleText { get; set; } = string.Empty;
}

public sealed class ExampleRewriteEnhancement
{
    public long DictionaryEntryExampleId { get; set; }
    public string OriginalExampleText { get; set; } = string.Empty;
    public string RewrittenExampleText { get; set; } = string.Empty;

    public string Model { get; set; } = "Regex+RewriteMap+Humanizer";
    public int Confidence { get; set; } = 100;
}