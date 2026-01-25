using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Core.Rewrite;

public interface IRewriteMapRepository
{
    Task<IReadOnlyList<RewriteMapRule>> GetRewriteRulesAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct);

    Task<IReadOnlyList<string>> GetStopWordsAsync(
        string sourceCode,
        RewriteTargetMode mode,
        CancellationToken ct);
}