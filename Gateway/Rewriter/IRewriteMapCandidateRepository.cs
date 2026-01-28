namespace DictionaryImporter.Gateway.Rewriter;

public interface IRewriteMapCandidateRepository
{
    Task UpsertCandidatesAsync(
        IReadOnlyList<RewriteMapCandidateUpsert> candidates,
        CancellationToken ct);

    Task<IReadOnlyList<RewriteMapCandidateRow>> GetApprovedCandidatesAsync(
        string sourceCode,
        int take,
        CancellationToken ct);

    Task MarkPromotedAsync(
        IReadOnlyList<long> candidateIds,
        string approvedBy,
        CancellationToken ct);

    // NEW METHOD (added)
    Task<IReadOnlySet<string>> GetExistingRewriteMapKeysAsync(
        string sourceCode,
        CancellationToken ct);
}